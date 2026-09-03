using Liquida.Shared.Contracts;

namespace Liquida.Producer.Source;

public interface ITransferSource
{
    bool Continuo { get; }

    Task PrepararAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LiquidacaoRequest>> LerProximaPaginaAsync(CancellationToken cancellationToken);

    Task MarcarEnviadaAsync(Guid id, CancellationToken cancellationToken);
}
