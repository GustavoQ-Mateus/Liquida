using Dapper;
using Liquida.Shared.Contracts;
using Npgsql;

namespace Liquida.Consumer.Data;

public interface ILiquidacaoRepository
{
    Task<bool> LiquidarAsync(LiquidacaoMessage message, CancellationToken cancellationToken);
}

public sealed class LiquidacaoRepository : ILiquidacaoRepository
{
    private const string InsertSql = """
        INSERT INTO liquidacoes (transacao_id, status, valor, liquidado_em)
        VALUES (@Id, 'LIQUIDADA', @Valor, now())
        ON CONFLICT (transacao_id) DO NOTHING;
        """;

    private readonly string _connectionString;

    public LiquidacaoRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
    }

    public async Task<bool> LiquidarAsync(LiquidacaoMessage message, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            InsertSql,
            new { message.Id, message.Valor },
            cancellationToken: cancellationToken);

        var rows = await connection.ExecuteAsync(command);
        return rows > 0;
    }
}
