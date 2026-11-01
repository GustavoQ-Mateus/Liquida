using Liquida.Consumer.Data;
using Liquida.Shared.BankCore;
using Liquida.Shared.Contracts;

namespace Liquida.Consumer;

public sealed class LiquidacaoProcessor
{
    private const int MaxTentativas = 3;
    private const string SettlementRefPrefixo = "liquida:";

    private readonly IServiceProvider _services;
    private readonly ILogger<LiquidacaoProcessor> _logger;

    private long _liquidadas;
    private long _duplicadas;

    public LiquidacaoProcessor(IServiceProvider services, ILogger<LiquidacaoProcessor> logger)
    {
        _services = services;
        _logger = logger;
    }

    public long Liquidadas => Interlocked.Read(ref _liquidadas);

    public long Duplicadas => Interlocked.Read(ref _duplicadas);

    public async Task<bool> ProcessarAsync(LiquidacaoMessage message)
    {
        for (var tentativa = 1; tentativa <= MaxTentativas; tentativa++)
        {
            try
            {
                var resultado = await TentarAsync(message);
                if (resultado is not null)
                {
                    return resultado.Value;
                }

                _logger.LogWarning("Confirmação de {TransacaoId} transitória (tentativa {Tentativa}/{Max}).",
                    message.Id, tentativa, MaxTentativas);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao processar {TransacaoId} (tentativa {Tentativa}/{Max}).",
                    message.Id, tentativa, MaxTentativas);
            }

            if (tentativa < MaxTentativas)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * tentativa));
            }
        }

        return false;
    }

    private async Task<bool?> TentarAsync(LiquidacaoMessage message)
    {
        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILiquidacaoRepository>();
        var settle = scope.ServiceProvider.GetService<IBankCoreSettleClient>();

        var estado = await repo.RegistrarAsync(message, CancellationToken.None);

        if (settle is null)
        {
            RegistrarLocal(estado, message);
            return true;
        }

        if (estado == RegistroLiquidacao.JaConfirmada)
        {
            Interlocked.Increment(ref _duplicadas);
            _logger.LogInformation("Transação {TransacaoId} já confirmada; ignorada (idempotência).", message.Id);
            return true;
        }

        var resultado = await settle.SettleAsync(message.Id, SettlementRefPrefixo + message.Id, CancellationToken.None);

        if (resultado == SettleResultado.Confirmada)
        {
            await repo.MarcarConfirmadaAsync(message.Id, CancellationToken.None);
            if (estado == RegistroLiquidacao.Inserida)
            {
                Interlocked.Increment(ref _liquidadas);
            }

            _logger.LogInformation("Transação {TransacaoId} liquidada e confirmada no BankCore.", message.Id);
            return true;
        }

        if (resultado == SettleResultado.Terminal)
        {
            _logger.LogError("Confirmação de {TransacaoId} falhou de forma terminal; enviada para a DLQ.", message.Id);
            return false;
        }

        return null;
    }

    private void RegistrarLocal(RegistroLiquidacao estado, LiquidacaoMessage message)
    {
        if (estado == RegistroLiquidacao.Inserida)
        {
            Interlocked.Increment(ref _liquidadas);
            _logger.LogInformation("Transação {TransacaoId} liquidada (valor={Valor} {Moeda}).",
                message.Id, message.Valor, message.Moeda);
            return;
        }

        Interlocked.Increment(ref _duplicadas);
        _logger.LogInformation("Transação {TransacaoId} já liquidada; ignorada (idempotência).", message.Id);
    }
}
