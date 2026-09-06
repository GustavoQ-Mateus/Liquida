using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Liquida.Shared.BankCore;

public enum SettleResultado
{
    Confirmada,
    Terminal,
    Transitorio
}

public interface IBankCoreSettleClient
{
    Task<SettleResultado> SettleAsync(Guid transferId, string settlementRef, CancellationToken cancellationToken);
}

public sealed class BankCoreSettleClient : IBankCoreSettleClient
{
    public const string ClientName = "bankcore-settle";

    private readonly HttpClient _http;
    private readonly ILogger<BankCoreSettleClient> _logger;

    public BankCoreSettleClient(HttpClient http, ILogger<BankCoreSettleClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<SettleResultado> SettleAsync(Guid transferId, string settlementRef, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PatchAsJsonAsync(
                $"/transfers/{transferId}/settle",
                new SettleRequest(settlementRef),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return SettleResultado.Confirmada;
            }

            return await ClassificarErroAsync(transferId, response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha de rede ao confirmar {TransferId}; tratando como transitório.", transferId);
            return SettleResultado.Transitorio;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout ao confirmar {TransferId}; tratando como transitório.", transferId);
            return SettleResultado.Transitorio;
        }
    }

    private async Task<SettleResultado> ClassificarErroAsync(
        Guid transferId, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = await LerCodigoAsync(response, cancellationToken);
        var status = (int)response.StatusCode;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Confirmação de {TransferId} retornou 401 mesmo após renovar; retry.", transferId);
            return SettleResultado.Transitorio;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError("Confirmação de {TransferId} retornou 403 (role sem SETTLEMENT?); DLQ + alerta.", transferId);
            return SettleResultado.Terminal;
        }

        if (status >= 500)
        {
            _logger.LogWarning("Confirmação de {TransferId} retornou {Status}; retry.", transferId, status);
            return SettleResultado.Transitorio;
        }

        _logger.LogError(
            "Confirmação de {TransferId} retornou {Status} code={Code}; terminal → DLQ.",
            transferId, status, code ?? "-");
        return SettleResultado.Terminal;
    }

    private async Task<string?> LerCodigoAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<BankCoreErrorEnvelope>(cancellationToken);
            return envelope?.Error?.Code;
        }
        catch
        {
            return null;
        }
    }
}
