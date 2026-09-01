using Dapper;
using Liquida.Consumer.Data;
using Liquida.Shared.Contracts;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Liquida.Tests;

public class ConsumerIdempotencyTests : IAsyncLifetime
{
    private const string SchemaDdl = """
        CREATE TABLE IF NOT EXISTS liquidacoes (
            transacao_id UUID PRIMARY KEY,
            status       TEXT          NOT NULL DEFAULT 'LIQUIDADA',
            valor        NUMERIC(18,2) NOT NULL,
            liquidado_em TIMESTAMPTZ   NOT NULL DEFAULT now()
        );
        """;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private LiquidacaoRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(SchemaDdl);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString()
            })
            .Build();

        _repository = new LiquidacaoRepository(config);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MesmaTransacao_Duas_Vezes_Gera_Um_Registro()
    {
        var msg = new LiquidacaoMessage(
            Guid.NewGuid(), 250.00m, "BRL", "a", "b", TipoTransacao.TED, DateTimeOffset.UtcNow);

        var primeira = await _repository.LiquidarAsync(msg, CancellationToken.None);
        var segunda = await _repository.LiquidarAsync(msg, CancellationToken.None);

        Assert.True(primeira);
        Assert.False(segunda);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var total = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM liquidacoes WHERE transacao_id = @Id", new { msg.Id });

        Assert.Equal(1, total);
    }
}
