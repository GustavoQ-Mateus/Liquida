namespace Liquida.Api.Reading;

/// <summary>Snapshot do pipeline num instante (spec-v1.1.0 §4.1).</summary>
public sealed record MetricsSnapshot(
    DateTimeOffset CapturadoEm,
    long Pendentes,
    long Enviadas,
    long Liquidadas,
    long RpsLiquidacao,
    int? Fila,
    int? Dlq,
    long RateLimited);

/// <summary>Contadores vindos do PostgreSQL (spec-v1.1.0 §3).</summary>
public sealed record ContadoresPostgres(
    long Pendentes,
    long Enviadas,
    long Liquidadas,
    long RpsLiquidacao);

public sealed record LiquidacaoRecente(
    Guid TransacaoId,
    decimal Valor,
    DateTimeOffset LiquidadoEm);

public sealed record TransacaoRecente(
    Guid Id,
    decimal Valor,
    string Moeda,
    string Tipo,
    string Status,
    DateTimeOffset CriadoEm);
