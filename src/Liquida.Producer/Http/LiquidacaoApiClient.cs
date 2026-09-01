using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Liquida.Shared.Contracts;

namespace Liquida.Producer.Http;

public sealed class LiquidacaoApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public LiquidacaoApiClient(HttpClient http) => _http = http;

    public async Task<HttpStatusCode> EnviarAsync(LiquidacaoRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("/liquidacoes", request, JsonOptions, cancellationToken);
        return response.StatusCode;
    }
}
