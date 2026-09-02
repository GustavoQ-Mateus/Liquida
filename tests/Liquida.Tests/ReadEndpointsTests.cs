using System.Net;
using System.Net.Http.Json;
using Liquida.Api.Messaging;
using Liquida.Api.Reading;
using Liquida.Shared.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Liquida.Tests;

// Endpoints de leitura da 1.1.0: provam CA9 (GET fora do rate limiter) e o formato do snapshot.
public class ReadEndpointsTests : IClassFixture<ReadEndpointsTests.ReadApiFactory>
{
    private readonly ReadApiFactory _factory;

    public ReadEndpointsTests(ReadApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Metrics_Nao_E_Rate_Limited_Em_Rajada()
    {
        var client = _factory.CreateClient();

        var tarefas = Enumerable.Range(0, 80)
            .Select(_ => client.GetAsync("/metrics"))
            .ToArray();

        var respostas = await Task.WhenAll(tarefas);

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.DoesNotContain(respostas, r => r.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Metrics_Devolve_Contadores_Do_Postgres()
    {
        var client = _factory.CreateClient();

        var snapshot = await client.GetFromJsonAsync<MetricsSnapshot>("/metrics");

        Assert.NotNull(snapshot);
        Assert.Equal(940, snapshot!.Pendentes);
        Assert.Equal(60, snapshot.Enviadas);
        Assert.Equal(55, snapshot.Liquidadas);
        Assert.Equal(24, snapshot.RpsLiquidacao);
        // Sem RabbitMQ Management API no teste, fila/dlq degradam para null (RNF9).
        Assert.Null(snapshot.Fila);
        Assert.Null(snapshot.Dlq);
    }

    public sealed class ReadApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<RabbitMqPublisher>();
                services.RemoveAll<IMessagePublisher>();
                services.AddSingleton<IMessagePublisher, FakePublisher>();

                services.RemoveAll<IMetricsRepository>();
                services.AddSingleton<IMetricsRepository, FakeMetricsRepository>();
            });
        }
    }

    private sealed class FakePublisher : IMessagePublisher
    {
        public Task PublishAsync(LiquidacaoMessage message, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeMetricsRepository : IMetricsRepository
    {
        public Task<ContadoresPostgres> LerContadoresAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ContadoresPostgres(940, 60, 55, 24));

        public Task<IReadOnlyList<LiquidacaoRecente>> LiquidacoesRecentesAsync(int limite, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LiquidacaoRecente>>(Array.Empty<LiquidacaoRecente>());

        public Task<IReadOnlyList<TransacaoRecente>> TransacoesRecentesAsync(int limite, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TransacaoRecente>>(Array.Empty<TransacaoRecente>());
    }
}
