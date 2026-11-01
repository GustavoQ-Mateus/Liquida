using Dapper;
using Npgsql;

namespace Liquida.Api.Simulation;

public interface IResetRepository
{
    Task<long> ContarLiquidacoesAsync(CancellationToken cancellationToken);
    Task LimparAsync(CancellationToken cancellationToken);
}

public sealed class ResetRepository : IResetRepository
{
    private const string ContarSql = "select count(*) from liquidacoes;";
    private const string LimparSql = "truncate table liquidacoes, transacoes_pendentes;";

    private readonly string _connectionString;

    public ResetRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
    }

    public async Task<long> ContarLiquidacoesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(ContarSql, cancellationToken: cancellationToken));
    }

    public async Task LimparAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(LimparSql, cancellationToken: cancellationToken));
    }
}
