namespace Liquida.Api.Reading;

public sealed class ReceivedCounter
{
    private long _total;

    public void Increment() => Interlocked.Increment(ref _total);

    public long Total => Interlocked.Read(ref _total);
}
