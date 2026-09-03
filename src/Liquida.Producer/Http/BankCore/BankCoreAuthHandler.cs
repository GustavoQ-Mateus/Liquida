using System.Net;
using System.Net.Http.Headers;

namespace Liquida.Producer.Http.BankCore;

public sealed class BankCoreAuthHandler : DelegatingHandler
{
    private readonly BankCoreTokenProvider _tokenProvider;

    public BankCoreAuthHandler(BankCoreTokenProvider tokenProvider) => _tokenProvider = tokenProvider;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(forceRefresh: false, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        token = await _tokenProvider.GetTokenAsync(forceRefresh: true, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
