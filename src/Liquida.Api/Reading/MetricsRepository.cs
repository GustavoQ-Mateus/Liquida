using Dapper;
using Npgsql;

namespace Liquida.Api.Reading;

public interface IMetricsRepository
{
    Task<ContadoresPostgres> LerContadoresAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LiquidacaoRecente>> LiquidacoesRecentesAsync(int limite, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransacaoRecente>> TransacoesRecentesAsync(int limite, CancellationToken cancellationToken);
}

public sealed class MetricsRepository : IMetricsRepository
{
    private const string ContadoresSql = """
        SELECT
          (SELECT count(*) FROM transacoes_pendentes WHERE status = 'PENDENTE')                          AS Pendentes,
          (SELECT count(*) FROM transacoes_pendentes WHERE status = 'ENVIADA')                           AS Enviadas,
          (SELECT count(*) FROM liquidacoes)                                                             AS Liquidadas,
          (SELECT count(*) FROM liquidacoes WHERE liquidado_em > now() - interval '1 second')           AS RpsLiquidacao;
        """;

    private const string LiquidacoesRecentesSql = """
        SELECT transacao_id AS TransacaoId, valor AS Valor, liquidado_em AS LiquidadoEm
        FROM liquidacoes
        ORDER BY liquidado_em DESC
        LIMIT @Limite;
        """;

    private const string TransacoesRecentesSql = """
        SELECT id AS Id, valor AS Valor, moeda AS Moeda, tipo AS Tipo, status AS Status, criado_em AS CriadoEm
        FROM transacoes_pendentes
        ORDER BY criado_em DESC
        LIMIT @Limite;
        """;

    private readonly string _connectionString;

    public MetricsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
    }

    public async Task<ContadoresPostgres> LerContadoresAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<ContadoresPostgres>(
            new CommandDefinition(ContadoresSql, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LiquidacaoRecente>> LiquidacoesRecentesAsync(int limite, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<LiquidacaoRecente>(
            new CommandDefinition(LiquidacoesRecentesSql, new { Limite = limite }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TransacaoRecente>> TransacoesRecentesAsync(int limite, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<TransacaoRecente>(
            new CommandDefinition(TransacoesRecentesSql, new { Limite = limite }, cancellationToken: cancellationToken));
        return rows.ToList();
    }
}
