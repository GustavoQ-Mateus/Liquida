namespace Liquida.Shared.Contracts;

public sealed record LiquidacaoRequest(
    Guid Id,
    decimal Valor,
    string Moeda,
    string ContaOrigem,
    string ContaDestino,
    TipoTransacao Tipo);
