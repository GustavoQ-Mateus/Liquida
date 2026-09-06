using Dapper;
using Liquida.Shared.Contracts;
using Npgsql;

namespace Liquida.Consumer.Data;

public enum RegistroLiquidacao
{
    Inserida,
    PendenteConfirmacao,
    JaConfirmada
}

public interface ILiquidacaoRepository
{
    Task<RegistroLiquidacao> RegistrarAsync(LiquidacaoMessage message, CancellationToken cancellationToken);
    Task MarcarConfirmadaAsync(Guid transacaoId, CancellationToken cancellationToken);
}

public sealed class LiquidacaoRepository : ILiquidacaoRepository
{
    private const string RegistrarSql = """
        WITH ins AS (
            INSERT INTO liquidacoes (transacao_id, status, valor, liquidado_em)
            VALUES (@Id, 'LIQUIDADA', @Valor, now())
            ON CONFLICT (transacao_id) DO NOTHING
            RETURNING transacao_id
        )
        SELECT 0 FROM ins
        UNION ALL
        SELECT CASE WHEN settle_confirmado_em IS NOT NULL THEN 2 ELSE 1 END
        FROM liquidacoes
        WHERE transacao_id = @Id AND NOT EXISTS (SELECT 1 FROM ins)
        LIMIT 1;
        """;

    private const string ConfirmarSql = """
        UPDATE liquidacoes SET settle_confirmado_em = now()
        WHERE transacao_id = @Id AND settle_confirmado_em IS NULL;
        """;

    private readonly string _connectionString;

    public LiquidacaoRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
    }

    public async Task<RegistroLiquidacao> RegistrarAsync(LiquidacaoMessage message, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            RegistrarSql,
            new { message.Id, message.Valor },
            cancellationToken: cancellationToken);

        var codigo = await connection.ExecuteScalarAsync<int>(command);
        return (RegistroLiquidacao)codigo;
    }

    public async Task MarcarConfirmadaAsync(Guid transacaoId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            ConfirmarSql,
            new { Id = transacaoId },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}
