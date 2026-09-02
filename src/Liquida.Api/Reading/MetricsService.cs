using Liquida.Shared.Messaging;

namespace Liquida.Api.Reading;

/// <summary>
/// Agrega o snapshot de <see cref="MetricsSnapshot"/> a partir das três fontes (ADR 0004),
/// sem deixar a falha de uma fonte derrubar as outras (RNF9).
/// </summary>
public sealed class MetricsService
{
    private readonly IMetricsRepository _repository;
    private readonly QueueDepthClient _queues;
    private readonly RateLimitCounter _rateLimited;
    private readonly TimeProvider _clock;

    public MetricsService(
        IMetricsRepository repository,
        QueueDepthClient queues,
        RateLimitCounter rateLimited,
        TimeProvider clock)
    {
        _repository = repository;
        _queues = queues;
        _rateLimited = rateLimited;
        _clock = clock;
    }

    public async Task<MetricsSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        var contadoresTask = _repository.LerContadoresAsync(cancellationToken);
        var filaTask = _queues.ProfundidadeAsync(RabbitMqTopology.Queue, cancellationToken);
        var dlqTask = _queues.ProfundidadeAsync(RabbitMqTopology.DeadLetterQueue, cancellationToken);

        await Task.WhenAll(contadoresTask, filaTask, dlqTask);

        var c = contadoresTask.Result;
        return new MetricsSnapshot(
            CapturadoEm: _clock.GetUtcNow(),
            Pendentes: c.Pendentes,
            Enviadas: c.Enviadas,
            Liquidadas: c.Liquidadas,
            RpsLiquidacao: c.RpsLiquidacao,
            Fila: filaTask.Result,
            Dlq: dlqTask.Result,
            RateLimited: _rateLimited.Total);
    }
}
