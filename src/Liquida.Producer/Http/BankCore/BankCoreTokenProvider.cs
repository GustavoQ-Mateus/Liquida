using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Liquida.Producer.Http.BankCore;

public sealed class BankCoreTokenProvider
{
    public const string AuthClientName = "bankcore-auth";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BankCoreOptions _options;
    private readonly ILogger<BankCoreTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt;

    public BankCoreTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<BankCoreOptions> options,
        ILogger<BankCoreTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && IsValid())
        {
            return _token!;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && IsValid())
            {
                return _token!;
            }

            var client = _httpClientFactory.CreateClient(AuthClientName);
            using var response = await client.PostAsJsonAsync(
                "/auth/token",
                new TokenRequest(_options.ClientId, _options.ClientSecret),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Resposta de /auth/token vazia.");

            var margin = Math.Max(_options.RefreshMarginSeconds, 5);
            _token = body.Token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(body.ExpiresIn - margin, 5));

            _logger.LogInformation("Token do BankCore renovado; válido por ~{Segundos}s.", body.ExpiresIn);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsValid() => _token is not null && DateTimeOffset.UtcNow < _expiresAt;
}
