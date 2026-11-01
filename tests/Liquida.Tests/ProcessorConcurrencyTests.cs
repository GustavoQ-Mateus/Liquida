using Dapper;
using Liquida.Consumer;
using Liquida.Consumer.Data;
using Liquida.Shared.BankCore;
using Liquida.Shared.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Liquida.Tests;

public class ProcessorConcurrencyTests : IAsyncLifetime
{
    private const string SchemaDdl = """
        CREATE TABLE IF NOT EXISTS liquidacoes (
            transacao_id         UUID PRIMARY KEY,
            status               TEXT          NOT NULL DEFAULT 'LIQUIDADA',
            valor                NUMERIC(18,2) NOT NULL,
            liquidado_em         TIMESTAMPTZ   NOT NULL DEFAULT now(),
            settle_confirmado_em TIMESTAMPTZ   NULL
        );
        """;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly SettleQueConfirma _settle = new();

    private LiquidacaoProcessor _processor = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.ExecuteAsync(SchemaDdl);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString()
            })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddScoped<ILiquidacaoRepository, LiquidacaoRepository>()
            .AddScoped<IBankCoreSettleClient>(_ => _settle)
            .BuildServiceProvider();
        _processor = new LiquidacaoProcessor(services, NullLogger<LiquidacaoProcessor>.Instance);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Processor_Concorrente_Mesmo_Id_Grava_Uma_Linha()
    {
        var message = new LiquidacaoMessage(Guid.NewGuid(), 42m, "BRL", "a", "b", null, DateTimeOffset.UtcNow);

        var resultados = await Task.WhenAll(Enumerable.Range(0, LiquidacaoConsumer.ProcessamentoParalelo)
            .Select(_ => Task.Run(() => _processor.ProcessarAsync(message))));

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        var linhas = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM liquidacoes WHERE transacao_id = @Id", new { message.Id });
        var confirmada = await connection.ExecuteScalarAsync<bool>(
            "SELECT settle_confirmado_em IS NOT NULL FROM liquidacoes WHERE transacao_id = @Id", new { message.Id });

        Assert.All(resultados, Assert.True);
        Assert.Equal(1, linhas);
        Assert.True(confirmada);
        Assert.Equal(1, _processor.Liquidadas);
    }

    private sealed class SettleQueConfirma : IBankCoreSettleClient
    {
        public Task<SettleResultado> SettleAsync(Guid transferId, string settlementRef, CancellationToken cancellationToken)
            => Task.FromResult(SettleResultado.Confirmada);
    }
}
