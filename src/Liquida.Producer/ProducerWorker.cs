using System.Net;
using System.Threading.RateLimiting;
using Liquida.Producer.Data;
using Liquida.Producer.Http;
using Microsoft.Extensions.Options;

namespace Liquida.Producer;

public sealed class ProducerWorker : BackgroundService
{
    private readonly ProducerOptions _options;
    private readonly IServiceProvider _services;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ProducerWorker> _logger;

    public ProducerWorker(
        IOptions<ProducerOptions> options,
        IServiceProvider services,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime lifetime,
        ILogger<ProducerWorker> logger)
    {
        _options = options.Value;
        _services = services;
        _httpClientFactory = httpClientFactory;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransacaoRepository>();

        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().EnsureSchemaAsync(stoppingToken);

        var pendentes = await repo.ContarPendentesAsync(stoppingToken);
        if (pendentes == 0 && _options.SeedCount > 0)
        {
            var semeadas = await repo.SemearAsync(_options.SeedCount, stoppingToken);
            _logger.LogInformation("Seed: {Semeadas} transações pendentes inseridas.", semeadas);
        }

        using var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = _options.RequestsPerSecond,
            TokensPerPeriod = _options.RequestsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = _options.RequestsPerSecond * 4,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });

        long lidas = 0, enviadas = 0, rateLimited = 0, falhas = 0;
        var inicio = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var pagina = await repo.LerPendentesAsync(_options.PageSize, stoppingToken);
            if (pagina.Count == 0)
            {
                break;
            }

            var client = new LiquidacaoApiClient(_httpClientFactory.CreateClient("liquidacoes"));

            foreach (var transacao in pagina)
            {
                lidas++;
                using var lease = await rateLimiter.AcquireAsync(1, stoppingToken);

                var status = await client.EnviarAsync(transacao.ToRequest(), stoppingToken);
                if (status == HttpStatusCode.Accepted)
                {
                    await repo.MarcarEnviadaAsync(transacao.Id, stoppingToken);
                    enviadas++;
                }
                else if (status == HttpStatusCode.TooManyRequests)
                {
                    rateLimited++;
                    _logger.LogWarning("Transação {Id} recebeu 429 após retries; será reenviada.", transacao.Id);
                }
                else
                {
                    falhas++;
                    _logger.LogError("Transação {Id} recebeu status inesperado {Status}.", transacao.Id, (int)status);
                }
            }
        }

        var duracao = DateTimeOffset.UtcNow - inicio;
        var rps = duracao.TotalSeconds > 0 ? enviadas / duracao.TotalSeconds : 0;
        _logger.LogInformation(
            "Producer concluído em {Segundos:F1}s. lidas={Lidas} enviadas={Enviadas} rate_limited={RateLimited} falhas={Falhas} vazao={Rps:F1}rps",
            duracao.TotalSeconds, lidas, enviadas, rateLimited, falhas, rps);

        _lifetime.StopApplication();
    }
}
