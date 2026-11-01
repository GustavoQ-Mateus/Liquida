using System.Net;
using Liquida.Producer;
using Liquida.Producer.Source;
using Liquida.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Liquida.Tests;

public class ProducerResilienceTests
{
    [Fact]
    public async Task Producer_Segue_Apos_Api_Indisponivel()
    {
        var source = new UmaTransferenciaSource();
        var api = new ApiQueFalhaDuasVezes();
        await using var services = new ServiceCollection()
            .AddScoped<ITransferSource>(_ => source)
            .BuildServiceProvider();
        var worker = new ProducerWorker(
            Options.Create(new ProducerOptions { RequestsPerSecond = 25, PollDelaySeconds = 1 }),
            services,
            new FabricaDeClientes(api),
            new LifetimeFalso(),
            NullLogger<ProducerWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var enviada = await Task.WhenAny(source.Enviada.Task, Task.Delay(TimeSpan.FromSeconds(15))) == source.Enviada.Task;
        await worker.StopAsync(CancellationToken.None);

        Assert.True(enviada);
        Assert.Equal(3, api.Chamadas);
    }

    private sealed class UmaTransferenciaSource : ITransferSource
    {
        private readonly LiquidacaoRequest _request = new(Guid.NewGuid(), 10m, "BRL", "a", "b", TipoTransacao.PIX);

        public TaskCompletionSource Enviada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Continuo => true;

        public Task PrepararAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<LiquidacaoRequest>> LerProximaPaginaAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LiquidacaoRequest>>(Enviada.Task.IsCompleted ? [] : [_request]);

        public Task MarcarEnviadaAsync(Guid id, CancellationToken cancellationToken)
        {
            Enviada.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class ApiQueFalhaDuasVezes : HttpMessageHandler
    {
        private int _chamadas;

        public int Chamadas => _chamadas;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _chamadas) <= 2)
            {
                throw new HttpRequestException("API fora do ar");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }
    }

    private sealed class FabricaDeClientes(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://liquida-api") };
    }

    private sealed class LifetimeFalso : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
