using Liquida.Producer.Data;
using Liquida.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace Liquida.Producer.Source;

public sealed class LocalTransferSource : ITransferSource
{
    private readonly ITransacaoRepository _repo;
    private readonly DatabaseInitializer _initializer;
    private readonly ProducerOptions _options;
    private readonly ILogger<LocalTransferSource> _logger;

    public LocalTransferSource(
        ITransacaoRepository repo,
        DatabaseInitializer initializer,
        IOptions<ProducerOptions> options,
        ILogger<LocalTransferSource> logger)
    {
        _repo = repo;
        _initializer = initializer;
        _options = options.Value;
        _logger = logger;
    }

    public bool Continuo => false;

    public async Task PrepararAsync(CancellationToken cancellationToken)
    {
        await _initializer.EnsureSchemaAsync(cancellationToken);

        var pendentes = await _repo.ContarPendentesAsync(cancellationToken);
        if (pendentes == 0 && _options.SeedCount > 0)
        {
            var semeadas = await _repo.SemearAsync(_options.SeedCount, cancellationToken);
            _logger.LogInformation("Seed: {Semeadas} transações pendentes inseridas.", semeadas);
        }
    }

    public async Task<IReadOnlyList<LiquidacaoRequest>> LerProximaPaginaAsync(CancellationToken cancellationToken)
    {
        var pagina = await _repo.LerPendentesAsync(_options.PageSize, cancellationToken);
        return pagina.Select(t => t.ToRequest()).ToList();
    }

    public Task MarcarEnviadaAsync(Guid id, CancellationToken cancellationToken) =>
        _repo.MarcarEnviadaAsync(id, cancellationToken);
}
