using Liquida.Consumer;
using Liquida.Consumer.Data;
using Liquida.Shared.BankCore;
using Liquida.Shared.Messaging;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<ILiquidacaoRepository, LiquidacaoRepository>();

if (builder.Configuration.GetValue<bool>("BankCore:CallbackAtivo"))
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

    builder.Services.AddHttpClient(BankCoreSettleClient.ClientName, client =>
        {
            client.BaseAddress = new Uri(bankCoreBaseUrl);
        })
        .AddHttpMessageHandler<BankCoreAuthHandler>();

    builder.Services.AddScoped<IBankCoreSettleClient>(sp => new BankCoreSettleClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(BankCoreSettleClient.ClientName),
        sp.GetRequiredService<ILogger<BankCoreSettleClient>>()));
}

builder.Services.AddHostedService<LiquidacaoConsumer>();

var host = builder.Build();
host.Run();
