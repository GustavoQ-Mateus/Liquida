using Liquida.Producer;
using Liquida.Producer.Data;
using Polly;
using Polly.Extensions.Http;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.Configure<ProducerOptions>(
    builder.Configuration.GetSection(ProducerOptions.SectionName));

builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<ITransacaoRepository, TransacaoRepository>();

var apiBaseUrl = builder.Configuration
    .GetSection(ProducerOptions.SectionName)["ApiBaseUrl"] ?? "http://localhost:5058";

builder.Services.AddHttpClient("liquidacoes", client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
    })
    .AddPolicyHandler(BuildRetryPolicy());

builder.Services.AddHostedService<ProducerWorker>();

var host = builder.Build();
host.Run();

static IAsyncPolicy<HttpResponseMessage> BuildRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => (int)response.StatusCode == 429)
        .WaitAndRetryAsync(
            retryCount: 5,
            sleepDurationProvider: (attempt, outcome, _) =>
            {
                var retryAfter = outcome.Result?.Headers.RetryAfter;
                if (retryAfter?.Delta is { } delta)
                {
                    return delta;
                }

                if (retryAfter?.Date is { } date)
                {
                    var wait = date - DateTimeOffset.UtcNow;
                    return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
                }

                return TimeSpan.FromMilliseconds(Math.Pow(2, attempt - 1) * 200);
            },
            onRetryAsync: (_, _, _, _) => Task.CompletedTask);
