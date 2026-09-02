namespace Liquida.Api.Reading;

/// <summary>
/// Contador em memória de respostas 429 desde o start da API (spec-v1.1.0 §3, ADR 0004).
/// É observabilidade, não estado de negócio: zera a cada restart, por design.
/// </summary>
public sealed class RateLimitCounter
{
    private long _total;

    public void Increment() => Interlocked.Increment(ref _total);

    public long Total => Interlocked.Read(ref _total);
}
