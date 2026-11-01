namespace Liquida.Api.Reading;

public sealed class ReceivedCounter
{
    private long _total;

    public void Increment() => Interlocked.Increment(ref _total);

    public void Reset() => Interlocked.Exchange(ref _total, 0);

    public long Total => Interlocked.Read(ref _total);
}
