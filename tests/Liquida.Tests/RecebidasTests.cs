using System.Net;
using System.Net.Http.Json;
using Liquida.Api.Reading;
using Liquida.Shared.Contracts;
using Xunit;

namespace Liquida.Tests;

public class RecebidasTests
{
    [Fact]
    public async Task Metrics_Devolve_Recebidas_Apos_Posts_Aceitos()
    {
        await using var factory = new ReadEndpointsTests.ReadApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var resposta = await client.PostAsJsonAsync("/liquidacoes", NovaLiquidacao());
            Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        }

        var snapshot = await client.GetFromJsonAsync<MetricsSnapshot>("/metrics");

        Assert.Equal(5, snapshot!.Recebidas);
    }

    [Fact]
    public async Task Recebidas_Nao_Conta_Requisicao_Invalida()
    {
        await using var factory = new ReadEndpointsTests.ReadApiFactory();
        var client = factory.CreateClient();

        var resposta = await client.PostAsJsonAsync("/liquidacoes", NovaLiquidacao() with { Valor = 0m });
        var snapshot = await client.GetFromJsonAsync<MetricsSnapshot>("/metrics");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(0, snapshot!.Recebidas);
    }

    [Fact]
    public async Task Recebidas_Nao_Conta_Rate_Limited()
    {
        await using var factory = new ReadEndpointsTests.ReadApiFactory();
        var client = factory.CreateClient();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 60)
            .Select(_ => client.PostAsJsonAsync("/liquidacoes", NovaLiquidacao())));
        var snapshot = await client.GetFromJsonAsync<MetricsSnapshot>("/metrics");

        var aceitas = respostas.Count(r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.Contains(respostas, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.Equal(aceitas, snapshot!.Recebidas);
    }

    private static LiquidacaoRequest NovaLiquidacao()
        => new(Guid.NewGuid(), 10m, "BRL", "a", "b", TipoTransacao.PIX);
}
