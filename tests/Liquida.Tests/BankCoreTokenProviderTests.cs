using System.Net;
using System.Text;
using Liquida.Shared.BankCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Liquida.Tests;

public class BankCoreTokenProviderTests
{
    private sealed class TokenHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var body = $$"""{"token":"jwt-{{Calls}}","token_type":"Bearer","expires_in":900}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _codes;
        public int Calls { get; private set; }

        public SequenceHandler(params HttpStatusCode[] codes) => _codes = new Queue<HttpStatusCode>(codes);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var code = _codes.Count > 0 ? _codes.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(code));
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://bankcore-test") };
    }

    private static BankCoreTokenProvider Provider(HttpMessageHandler handler) => new(
        new SingleClientFactory(handler),
        Options.Create(new BankCoreOptions { ClientId = "svc", ClientSecret = "x" }),
        NullLogger<BankCoreTokenProvider>.Instance);

    [Fact]
    public async Task Token_ECacheadoEntreChamadas()
    {
        var handler = new TokenHandler();
        var provider = Provider(handler);

        var primeiro = await provider.GetTokenAsync(forceRefresh: false, CancellationToken.None);
        var segundo = await provider.GetTokenAsync(forceRefresh: false, CancellationToken.None);

        Assert.Equal(primeiro, segundo);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ForceRefresh_ReemiteToken()
    {
        var handler = new TokenHandler();
        var provider = Provider(handler);

        var primeiro = await provider.GetTokenAsync(forceRefresh: false, CancellationToken.None);
        var renovado = await provider.GetTokenAsync(forceRefresh: true, CancellationToken.None);

        Assert.NotEqual(primeiro, renovado);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AuthHandler_Em401_RenovaERepeteUmaVez()
    {
        var provider = Provider(new TokenHandler());
        var inner = new SequenceHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        var auth = new BankCoreAuthHandler(provider) { InnerHandler = inner };
        using var client = new HttpClient(auth) { BaseAddress = new Uri("http://bankcore-test") };

        using var response = await client.GetAsync("/settlement/transfers?status=PENDING&page=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }
}
