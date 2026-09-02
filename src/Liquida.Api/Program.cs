using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using Liquida.Api.Messaging;
using Liquida.Api.Reading;
using Liquida.Api.Validation;
using Liquida.Shared.Contracts;
using Liquida.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMqPublisher>());

// Leitura para o dashboard (spec-v1.1.0, ADR 0004).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RateLimitCounter>();
builder.Services.AddSingleton<IMetricsRepository, MetricsRepository>();
builder.Services.AddHttpClient<QueueDepthClient>();
builder.Services.AddScoped<MetricsService>();

const string DashboardCors = "dashboard";
var dashboardOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200" };
builder.Services.AddCors(options =>
{
    options.AddPolicy(DashboardCors, policy => policy
        .WithOrigins(dashboardOrigins)
        .WithMethods("GET")
        .AllowAnyHeader());
});

const string LiquidacoesPolicy = "liquidacoes";

builder.Services.AddRateLimiter(options =>
{
    options.AddTokenBucketLimiter(LiquidacoesPolicy, limiter =>
    {
        limiter.TokenLimit = 25;
        limiter.TokensPerPeriod = 25;
        limiter.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.RequestServices.GetRequiredService<RateLimitCounter>().Increment();
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "1";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "rate_limited", message = "Limite de 25 req/s excedido." },
            cancellationToken);
    };
});

var app = builder.Build();

app.UseRateLimiter();
app.UseCors(DashboardCors);

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Endpoints de leitura do dashboard — fora do rate limiter (RNF8/CA9).
app.MapGet("/metrics", async (MetricsService metrics, CancellationToken ct) =>
    Results.Ok(await metrics.SnapshotAsync(ct)));

app.MapGet("/liquidacoes/recentes", async (
        IMetricsRepository repo, CancellationToken ct, int limite = 50) =>
    Results.Ok(await repo.LiquidacoesRecentesAsync(NormalizarLimite(limite), ct)));

app.MapGet("/transacoes/recentes", async (
        IMetricsRepository repo, CancellationToken ct, int limite = 50) =>
    Results.Ok(await repo.TransacoesRecentesAsync(NormalizarLimite(limite), ct)));

app.MapPost("/liquidacoes", async (
        LiquidacaoRequest request,
        IMessagePublisher publisher,
        CancellationToken cancellationToken) =>
    {
        if (!LiquidacaoRequestValidator.TryValidate(request, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        var message = LiquidacaoMessage.FromRequest(request);
        await publisher.PublishAsync(message, cancellationToken);

        return Results.Accepted($"/liquidacoes/{request.Id}");
    })
    .RequireRateLimiting(LiquidacoesPolicy);

app.Run();

static int NormalizarLimite(int limite) => Math.Clamp(limite, 1, 200);

public partial class Program;
