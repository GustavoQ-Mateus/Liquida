using Liquida.Shared.Contracts;

namespace Liquida.Producer.Data;

public sealed record TransacaoPendente(
    Guid Id,
    decimal Valor,
    string Moeda,
    string ContaOrigem,
    string ContaDestino,
    TipoTransacao Tipo)
{
    public LiquidacaoRequest ToRequest() => new(Id, Valor, Moeda, ContaOrigem, ContaDestino, Tipo);
}
