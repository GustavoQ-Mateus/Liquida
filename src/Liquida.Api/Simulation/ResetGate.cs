namespace Liquida.Api.Simulation;

public sealed class ResetGate
{
    private const int Aberto = 0;
    private const int Fechado = 1;

    private int _estado;
    private long _barradas;

    public bool EstaFechado => Volatile.Read(ref _estado) == Fechado;

    public long Barradas => Interlocked.Read(ref _barradas);

    public bool TentarFechar() => Interlocked.CompareExchange(ref _estado, Fechado, Aberto) == Aberto;

    public void Abrir() => Volatile.Write(ref _estado, Aberto);

    public void RegistrarBarrada() => Interlocked.Increment(ref _barradas);
}
