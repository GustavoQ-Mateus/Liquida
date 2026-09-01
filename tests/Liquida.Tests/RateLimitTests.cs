using System.Net;
using System.Net.Http.Json;
using Liquida.Api.Messaging;
using Liquida.Shared.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Liquida.Tests;

public class RateLimitTests : IClassFixture<RateLimitTests.ApiFactory>
{
    private readonly ApiFactory _factory;

    public RateLimitTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Rajada_Respeita_25_Por_Segundo()
    {
        var client = _factory.CreateClient();

        var payload = new LiquidacaoRequest(
            Guid.NewGuid(), 10m, "BRL", "a", "b", TipoTransacao.PIX);

        var tarefas = Enumerable.Range(0, 80)
            .Select(_ => client.PostAsJsonAsync("/liquidacoes", payload))
            .ToArray();

        var respostas = await Task.WhenAll(tarefas);

        var aceitas = respostas.Count(r => r.StatusCode == HttpStatusCode.Accepted);
        var limitadas = respostas.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToArray();

        Assert.Equal(80, aceitas + limitadas.Length);
        Assert.InRange(aceitas, 25, 27);
        Assert.NotEmpty(limitadas);
        Assert.All(limitadas, r =>
            Assert.Contains("1", r.Headers.GetValues("Retry-After")));
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<RabbitMqPublisher>();
                services.RemoveAll<IMessagePublisher>();
                services.AddSingleton<IMessagePublisher, FakePublisher>();
            });
        }
    }

    private sealed class FakePublisher : IMessagePublisher
    {
        public Task PublishAsync(LiquidacaoMessage message, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
