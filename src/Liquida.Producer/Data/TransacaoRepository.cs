using Dapper;
using Liquida.Shared.Contracts;
using Npgsql;

namespace Liquida.Producer.Data;

public interface ITransacaoRepository
{
    Task<int> ContarPendentesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TransacaoPendente>> LerPendentesAsync(int limite, CancellationToken cancellationToken);
    Task MarcarEnviadaAsync(Guid id, CancellationToken cancellationToken);
    Task<int> SemearAsync(int quantidade, CancellationToken cancellationToken);
}

public sealed class TransacaoRepository : ITransacaoRepository
{
    private const string SelectPendentesSql = """
        SELECT id            AS Id,
               valor         AS Valor,
               moeda         AS Moeda,
               conta_origem  AS ContaOrigem,
               conta_destino AS ContaDestino,
               tipo          AS Tipo
        FROM transacoes_pendentes
        WHERE status = 'PENDENTE'
        ORDER BY criado_em, id
        LIMIT @Limite;
        """;

    private const string MarcarEnviadaSql =
        "UPDATE transacoes_pendentes SET status = 'ENVIADA' WHERE id = @Id;";

    private const string InsertSeedSql = """
        INSERT INTO transacoes_pendentes (id, valor, moeda, conta_origem, conta_destino, tipo, status)
        VALUES (@Id, @Valor, 'BRL', @ContaOrigem, @ContaDestino, @Tipo, 'PENDENTE');
        """;

    private readonly string _connectionString;

    public TransacaoRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
    }

    public async Task<int> ContarPendentesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM transacoes_pendentes WHERE status = 'PENDENTE';",
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TransacaoPendente>> LerPendentesAsync(int limite, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<TransacaoPendente>(new CommandDefinition(
            SelectPendentesSql, new { Limite = limite }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task MarcarEnviadaAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            MarcarEnviadaSql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<int> SemearAsync(int quantidade, CancellationToken cancellationToken)
    {
        var tipos = new[] { TipoTransacao.PIX, TipoTransacao.TED, TipoTransacao.BOLETO };
        var rnd = new Random();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        for (var i = 0; i < quantidade; i++)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertSeedSql,
                new
                {
                    Id = Guid.NewGuid(),
                    Valor = Math.Round((decimal)(rnd.NextDouble() * 1000 + 1), 2),
                    ContaOrigem = $"acc-{rnd.Next(1000, 9999)}",
                    ContaDestino = $"acc-{rnd.Next(1000, 9999)}",
                    Tipo = tipos[rnd.Next(tipos.Length)].ToString()
                },
                transaction: transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return quantidade;
    }
}
