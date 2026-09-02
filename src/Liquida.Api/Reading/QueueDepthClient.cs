using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Liquida.Shared.Messaging;
using Microsoft.Extensions.Options;

namespace Liquida.Api.Reading;

/// <summary>
/// Lê a profundidade das filas via RabbitMQ Management API (ADR 0004).
/// Degrada para null quando o Management API está indisponível (RNF9).
/// </summary>
public sealed class QueueDepthClient
{
    private readonly HttpClient _http;
    private readonly ILogger<QueueDepthClient> _logger;

    public QueueDepthClient(HttpClient http, IOptions<RabbitMqOptions> options, ILogger<QueueDepthClient> logger)
    {
        var opt = options.Value;
        _http = http;
        _http.BaseAddress = new Uri($"http://{opt.Host}:{opt.ManagementPort}/");
        _http.Timeout = TimeSpan.FromSeconds(2);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opt.User}:{opt.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _logger = logger;
    }

    /// <summary>Profundidade (mensagens prontas + não-ack) da fila, ou null se indisponível.</summary>
    public async Task<int?> ProfundidadeAsync(string queue, CancellationToken cancellationToken)
    {
        try
        {
            // vhost "/" precisa ser codificado como %2F no path do Management API.
            using var response = await _http.GetAsync($"api/queues/%2F/{queue}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return doc.RootElement.TryGetProperty("messages", out var messages)
                ? messages.GetInt32()
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RabbitMQ Management API indisponível ao ler a fila '{Queue}'.", queue);
            return null;
        }
    }
}
