using System.Net;
using System.Text;
using Liquida.Shared.BankCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liquida.Tests;

public class BankCoreSettleClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string? _body;

        public StubHandler(HttpStatusCode code, string? body = null)
        {
            _code = code;
            _body = body;
        }

        public string? UltimoCaminho { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimoCaminho = request.RequestUri?.AbsolutePath;
            var response = new HttpResponseMessage(_code);
            if (_body is not null)
            {
                response.Content = new StringContent(_body, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private static BankCoreSettleClient Client(StubHandler handler) => new(
        new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://bankcore-test") },
        NullLogger<BankCoreSettleClient>.Instance);

    [Fact]
    public async Task Http200_Confirmada()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":"x","status":"SETTLED"}""");
        var id = Guid.NewGuid();

        var resultado = await Client(handler).SettleAsync(id, "liquida:" + id, CancellationToken.None);

        Assert.Equal(SettleResultado.Confirmada, resultado);
        Assert.Equal($"/transfers/{id}/settle", handler.UltimoCaminho);
    }

    [Fact]
    public async Task Http409_SettleOnFailed_Terminal()
    {
        var handler = new StubHandler(HttpStatusCode.Conflict,
            """{"error":{"code":"SETTLE_ON_FAILED","message":"transferência falhou"}}""");

        var resultado = await Client(handler).SettleAsync(Guid.NewGuid(), "ref", CancellationToken.None);

        Assert.Equal(SettleResultado.Terminal, resultado);
    }

    [Fact]
    public async Task Http404_Terminal()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound);

        var resultado = await Client(handler).SettleAsync(Guid.NewGuid(), "ref", CancellationToken.None);

        Assert.Equal(SettleResultado.Terminal, resultado);
    }

    [Fact]
    public async Task Http403_Terminal()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden);

        var resultado = await Client(handler).SettleAsync(Guid.NewGuid(), "ref", CancellationToken.None);

        Assert.Equal(SettleResultado.Terminal, resultado);
    }

    [Fact]
    public async Task Http401_Transitorio()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized);

        var resultado = await Client(handler).SettleAsync(Guid.NewGuid(), "ref", CancellationToken.None);

        Assert.Equal(SettleResultado.Transitorio, resultado);
    }

    [Fact]
    public async Task Http503_Transitorio()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable);

        var resultado = await Client(handler).SettleAsync(Guid.NewGuid(), "ref", CancellationToken.None);

        Assert.Equal(SettleResultado.Transitorio, resultado);
    }
}
