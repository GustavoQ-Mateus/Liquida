using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Liquida.Api.Reading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Liquida.Tests;

public class MetricsStreamTests : IClassFixture<ReadEndpointsTests.ReadApiFactory>
{
    private const string DashboardOrigin = "http://localhost:4200";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly ReadEndpointsTests.ReadApiFactory _factory;

    public MetricsStreamTests(ReadEndpointsTests.ReadApiFactory factory) => _factory = factory;

    [Fact]
    public async Task MetricsStream_Emite_Evento_Metrics_Por_Segundo()
    {
        var client = _factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync("/metrics/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        var relogio = Stopwatch.StartNew();
        var primeiro = await LerEventoAsync(reader, cts.Token);
        var instantePrimeiro = relogio.Elapsed;
        var segundo = await LerEventoAsync(reader, cts.Token);
        var intervalo = relogio.Elapsed - instantePrimeiro;

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(940, primeiro.Pendentes);
        Assert.Equal(55, segundo.Liquidadas);
        Assert.InRange(intervalo, TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(1800));
    }

    [Fact]
    public async Task MetricsStream_Encerra_Ao_Cancelar_Conexao()
    {
        using var scope = _factory.Services.CreateScope();
        var metrics = scope.ServiceProvider.GetRequiredService<MetricsService>();
        var jsonOptions = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
        var context = new DefaultHttpContext { RequestAborted = cts.Token };
        context.Response.Body = new MemoryStream();

        var escrita = MetricsStream.WriteAsync(context, metrics, jsonOptions);
        var terminou = await Task.WhenAny(escrita, Task.Delay(TimeSpan.FromSeconds(5))) == escrita;

        Assert.True(terminou);
        Assert.True(escrita.IsCompletedSuccessfully);
        Assert.Contains("event: metrics", LerCorpo(context));
    }

    [Fact]
    public async Task MetricsStream_Responde_Com_Cabecalho_Cors_Do_Dashboard()
    {
        var client = _factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics/stream");
        request.Headers.Add("Origin", DashboardOrigin);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(DashboardOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task MetricsStream_Nao_E_Rate_Limited()
    {
        var client = _factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var respostas = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(_ => client.GetAsync("/metrics/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token)));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }
    }

    private static async Task<MetricsSnapshot> LerEventoAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var evento = await reader.ReadLineAsync(cancellationToken);
        var dados = await reader.ReadLineAsync(cancellationToken);
        await reader.ReadLineAsync(cancellationToken);
        Assert.Equal("event: metrics", evento);
        return JsonSerializer.Deserialize<MetricsSnapshot>(dados!["data: ".Length..], WebJson)!;
    }

    private static string LerCorpo(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }
}
