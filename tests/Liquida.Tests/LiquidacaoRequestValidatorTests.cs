using Liquida.Api.Validation;
using Liquida.Shared.Contracts;
using Xunit;

namespace Liquida.Tests;

public class LiquidacaoRequestValidatorTests
{
    private static LiquidacaoRequest Valida() => new(
        Guid.NewGuid(), 100.50m, "BRL", "acc-1", "acc-2", TipoTransacao.PIX);

    [Fact]
    public void RequestValida_Passa()
    {
        var ok = LiquidacaoRequestValidator.TryValidate(Valida(), out var errors);
        Assert.True(ok);
        Assert.Empty(errors);
    }

    [Fact]
    public void RequestNula_Falha()
    {
        var ok = LiquidacaoRequestValidator.TryValidate(null, out var errors);
        Assert.False(ok);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValorZeroOuNegativo_Falha()
    {
        var ok = LiquidacaoRequestValidator.TryValidate(Valida() with { Valor = 0 }, out var errors);
        Assert.False(ok);
        Assert.Contains(nameof(LiquidacaoRequest.Valor), errors.Keys);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("REAL")]
    [InlineData("")]
    public void MoedaInvalida_Falha(string moeda)
    {
        var ok = LiquidacaoRequestValidator.TryValidate(Valida() with { Moeda = moeda }, out var errors);
        Assert.False(ok);
        Assert.Contains(nameof(LiquidacaoRequest.Moeda), errors.Keys);
    }

    [Fact]
    public void IdVazio_Falha()
    {
        var ok = LiquidacaoRequestValidator.TryValidate(Valida() with { Id = Guid.Empty }, out var errors);
        Assert.False(ok);
        Assert.Contains(nameof(LiquidacaoRequest.Id), errors.Keys);
    }
}
