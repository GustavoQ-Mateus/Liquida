using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using Liquida.Api.Messaging;
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
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "1";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "rate_limited", message = "Limite de 25 req/s excedido." },
            cancellationToken);
    };
});

var app = builder.Build();

app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

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

public partial class Program;
