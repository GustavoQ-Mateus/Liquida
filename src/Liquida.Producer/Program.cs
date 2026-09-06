using Liquida.Producer;
using Liquida.Producer.Data;
using Liquida.Producer.Http.BankCore;
using Liquida.Producer.Source;
using Liquida.Shared.BankCore;
using Polly;
using Polly.Extensions.Http;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var producerSection = builder.Configuration.GetSection(ProducerOptions.SectionName);
builder.Services.Configure<ProducerOptions>(producerSection);

builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<ITransacaoRepository, TransacaoRepository>();

var apiBaseUrl = producerSection["ApiBaseUrl"] ?? "http://localhost:5058";

builder.Services.AddHttpClient("liquidacoes", client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
    })
    .AddPolicyHandler(BuildRetryPolicy());

var origem = producerSection.GetValue<TransferOrigem>("Origem");
if (origem == TransferOrigem.BankCore)
{
    builder.Services.Configure<BankCoreOptions>(builder.Configuration.GetSection(BankCoreOptions.SectionName));

    var bankCoreBaseUrl = builder.Configuration
        .GetSection(BankCoreOptions.SectionName)["BaseUrl"] ?? "http://bankcore-api:8080";

    builder.Services.AddSingleton<BankCoreTokenProvider>();
    builder.Services.AddTransient<BankCoreAuthHandler>();

    builder.Services.AddHttpClient(BankCoreTokenProvider.AuthClientName, client =>
    {
        client.BaseAddress = new Uri(bankCoreBaseUrl);
    });

    builder.Services.AddHttpClient(BankCoreClient.ClientName, client =>
        {
            client.BaseAddress = new Uri(bankCoreBaseUrl);
        })
        .AddHttpMessageHandler<BankCoreAuthHandler>();

    builder.Services.AddScoped(sp => new BankCoreClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(BankCoreClient.ClientName)));

    builder.Services.AddScoped<ITransferSource, BankCoreTransferSource>();
}
else
{
    builder.Services.AddScoped<ITransferSource, LocalTransferSource>();
}

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
