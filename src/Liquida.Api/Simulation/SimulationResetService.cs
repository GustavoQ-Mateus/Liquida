using Liquida.Api.Reading;
using Liquida.Shared.Messaging;

namespace Liquida.Api.Simulation;

public enum ResultadoReset
{
    Concluido,
    EmAndamento,
    NaoEstabilizou
}

public sealed class SimulationResetService
{
    public static readonly TimeSpan JanelaDeSilencio = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan IntervaloDeObservacao = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan TempoMaximoDeReset = TimeSpan.FromSeconds(20);

    private readonly ResetGate _gate;
    private readonly IQueueMaintenance _queues;
    private readonly IResetRepository _repository;
    private readonly ReceivedCounter _received;
    private readonly RateLimitCounter _rateLimited;
    private readonly TimeProvider _clock;
    private readonly ILogger<SimulationResetService> _logger;

    public SimulationResetService(
        ResetGate gate,
        IQueueMaintenance queues,
        IResetRepository repository,
        ReceivedCounter received,
        RateLimitCounter rateLimited,
        TimeProvider clock,
        ILogger<SimulationResetService> logger)
    {
        _gate = gate;
        _queues = queues;
        _repository = repository;
        _received = received;
        _rateLimited = rateLimited;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ResultadoReset> ResetarAsync(CancellationToken cancellationToken)
    {
        if (!_gate.TentarFechar())
        {
            return ResultadoReset.EmAndamento;
        }

        try
        {
            await using var filas = await _queues.ConectarAsync(cancellationToken);
            var estabilizou = await EsperarSilencioAsync(filas, cancellationToken);
            await filas.PurgarAsync(RabbitMqTopology.Queue, cancellationToken);
            await filas.PurgarAsync(RabbitMqTopology.DeadLetterQueue, cancellationToken);
            await _repository.LimparAsync(cancellationToken);
            _received.Reset();
            _rateLimited.Reset();
            _logger.LogInformation("Ambiente de simulação limpo (estabilizou={Estabilizou}).", estabilizou);
            return estabilizou ? ResultadoReset.Concluido : ResultadoReset.NaoEstabilizou;
        }
        finally
        {
            _gate.Abrir();
        }
    }

    private async Task<bool> EsperarSilencioAsync(IQueueSession filas, CancellationToken cancellationToken)
    {
        var inicio = _clock.GetUtcNow();
        var silencioDesde = inicio;
        Sinais? anteriores = null;

        while (_clock.GetUtcNow() - inicio < TempoMaximoDeReset)
        {
            var purgadas = await filas.PurgarAsync(RabbitMqTopology.Queue, cancellationToken);
            var atuais = new Sinais(
                _gate.Barradas,
                await _repository.ContarLiquidacoesAsync(cancellationToken),
                await filas.ContarAsync(RabbitMqTopology.DeadLetterQueue, cancellationToken));
            var agora = _clock.GetUtcNow();

            if (purgadas > 0 || atuais != anteriores)
            {
                anteriores = atuais;
                silencioDesde = agora;
            }
            else if (agora - silencioDesde >= JanelaDeSilencio)
            {
                return true;
            }

            await Task.Delay(IntervaloDeObservacao, _clock, cancellationToken);
        }

        return false;
    }

    private sealed record Sinais(long Barradas, long Liquidacoes, uint Dlq);
}
