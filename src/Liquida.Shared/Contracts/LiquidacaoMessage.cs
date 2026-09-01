namespace Liquida.Shared.Contracts;

public sealed record LiquidacaoMessage(
    Guid Id,
    decimal Valor,
    string Moeda,
    string ContaOrigem,
    string ContaDestino,
    TipoTransacao Tipo,
    DateTimeOffset EnfileiradoEm)
{
    public static LiquidacaoMessage FromRequest(LiquidacaoRequest req) => new(
        req.Id,
        req.Valor,
        req.Moeda,
        req.ContaOrigem,
        req.ContaDestino,
        req.Tipo,
        DateTimeOffset.UtcNow);
}
