using Liquida.Producer.Http.BankCore;
using Liquida.Shared.Contracts;

namespace Liquida.Producer.Source;

public sealed class BankCoreTransferSource : ITransferSource
{
    private readonly BankCoreClient _client;
    private readonly HashSet<Guid> _despachadas = [];

    private int _proximaPagina = 1;

    public BankCoreTransferSource(BankCoreClient client) => _client = client;

    public bool Continuo => true;

    public Task PrepararAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<IReadOnlyList<LiquidacaoRequest>> LerProximaPaginaAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pagina = await _client.ListarPendentesAsync(_proximaPagina, cancellationToken);

            var novas = pagina.Transfers
                .Where(t => !_despachadas.Contains(t.Id))
                .Select(BankCoreClient.ParaRequest)
                .ToList();

            if (pagina.HasNext)
            {
                _proximaPagina++;
            }
            else
            {
                _proximaPagina = 1;
            }

            if (novas.Count > 0)
            {
                return novas;
            }

            if (!pagina.HasNext)
            {
                return [];
            }
        }
    }

    public Task MarcarEnviadaAsync(Guid id, CancellationToken cancellationToken)
    {
        _despachadas.Add(id);
        return Task.CompletedTask;
    }
}
