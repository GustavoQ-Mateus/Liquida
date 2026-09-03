using System.Net.Http.Json;
using Liquida.Shared.Contracts;

namespace Liquida.Producer.Http.BankCore;

public sealed class BankCoreClient
{
    public const string ClientName = "bankcore";

    private readonly HttpClient _http;

    public BankCoreClient(HttpClient http) => _http = http;

    public async Task<TransfersPage> ListarPendentesAsync(int page, CancellationToken cancellationToken)
    {
        var pagina = await _http.GetFromJsonAsync<TransfersPage>(
            $"/settlement/transfers?status=PENDING&page={page}", cancellationToken);

        return pagina ?? throw new InvalidOperationException("Resposta de /settlement/transfers vazia.");
    }

    public static LiquidacaoRequest ParaRequest(TransferDto transfer) => new(
        transfer.Id,
        transfer.AmountCents / 100m,
        "BRL",
        transfer.FromAccountId,
        transfer.ToAccountId,
        Tipo: null);
}
