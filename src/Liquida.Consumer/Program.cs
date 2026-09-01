using Liquida.Consumer;
using Liquida.Consumer.Data;
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
builder.Services.AddHostedService<LiquidacaoConsumer>();

var host = builder.Build();
host.Run();
