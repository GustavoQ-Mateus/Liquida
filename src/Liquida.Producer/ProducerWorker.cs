using System.Net;
using System.Threading.RateLimiting;
using Liquida.Producer.Http;
using Liquida.Producer.Source;
using Liquida.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace Liquida.Producer;

public sealed class ProducerWorker : BackgroundService
{
    private static readonly TimeSpan BackoffInicial = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BackoffMaximo = TimeSpan.FromSeconds(30);

    private readonly ProducerOptions _options;
    private readonly IServiceProvider _services;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ProducerWorker> _logger;

    private long _lidas;
    private long _enviadas;
    private long _rateLimited;
    private long _falhas;

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
        var source = scope.ServiceProvider.GetRequiredService<ITransferSource>();

        await source.PrepararAsync(stoppingToken);

        using var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = _options.RequestsPerSecond,
            TokensPerPeriod = _options.RequestsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = _options.RequestsPerSecond * 4,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });

        var pollDelay = TimeSpan.FromSeconds(Math.Max(_options.PollDelaySeconds, 1));
        var inicio = DateTimeOffset.UtcNow;
        var backoff = BackoffInicial;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pagina = await source.LerProximaPaginaAsync(stoppingToken);
                if (pagina.Count == 0)
                {
                    if (!source.Continuo)
                    {
                        break;
                    }

                    await Task.Delay(pollDelay, stoppingToken);
                    continue;
                }

                if (await EnviarPaginaAsync(source, pagina, rateLimiter, stoppingToken))
                {
                    backoff = BackoffInicial;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Falha no ciclo do Producer; nova tentativa em {Segundos}s.", backoff.TotalSeconds);
                await Task.Delay(backoff, stoppingToken);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, BackoffMaximo.Ticks));
            }
        }

        var duracao = DateTimeOffset.UtcNow - inicio;
        var rps = duracao.TotalSeconds > 0 ? _enviadas / duracao.TotalSeconds : 0;
        _logger.LogInformation(
            "Producer concluído em {Segundos:F1}s. lidas={Lidas} enviadas={Enviadas} rate_limited={RateLimited} falhas={Falhas} vazao={Rps:F1}rps",
            duracao.TotalSeconds, _lidas, _enviadas, _rateLimited, _falhas, rps);

        if (!source.Continuo)
        {
            _lifetime.StopApplication();
        }
    }

    private async Task<bool> EnviarPaginaAsync(
        ITransferSource source,
        IReadOnlyList<LiquidacaoRequest> pagina,
        RateLimiter rateLimiter,
        CancellationToken stoppingToken)
    {
        var client = new LiquidacaoApiClient(_httpClientFactory.CreateClient("liquidacoes"));
        var algumaAceita = false;

        foreach (var request in pagina)
        {
            _lidas++;
            using var lease = await rateLimiter.AcquireAsync(1, stoppingToken);

            var status = await client.EnviarAsync(request, stoppingToken);
            if (status == HttpStatusCode.Accepted)
            {
                await source.MarcarEnviadaAsync(request.Id, stoppingToken);
                _enviadas++;
                algumaAceita = true;
            }
            else if (status == HttpStatusCode.TooManyRequests)
            {
                _rateLimited++;
                _logger.LogWarning("Transação {Id} recebeu 429 após retries; será reenviada.", request.Id);
            }
            else
            {
                _falhas++;
                _logger.LogError("Transação {Id} recebeu status inesperado {Status}.", request.Id, (int)status);
            }
        }

        return algumaAceita;
    }
}
