namespace VortexPad.Core.Engine;

/// <summary>
/// Lock-free triple buffer for one producer and one consumer (SPSC), with no allocations after construction.
/// The producer (1000 Hz loop) writes to <see cref="WriteBuffer"/> and calls <see cref="Publish"/>;
/// the consumer (UI timer) calls <see cref="ReadLatest"/> and gets the most recent complete frame,
/// never one that is half-written. Frames the consumer does not get to read are dropped.
/// </summary>
public sealed class TripleBuffer<T> where T : class
{
    private const int FreshFlag = 0b100;
    private const int IndexMask = 0b011;

    private readonly T[] _buffers;
    private int _backIndex;   // producer only
    private int _frontIndex;  // consumer only
    private int _middleState; // middle buffer index | FreshFlag if it holds a frame not yet read

    public TripleBuffer(Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _buffers = new[] { factory(), factory(), factory() };
        _backIndex = 0;
        _middleState = 1;
        _frontIndex = 2;
    }

    /// <summary>Buffer the producer may write to. Only valid from the producer thread until <see cref="Publish"/>.</summary>
    public T WriteBuffer => _buffers[_backIndex];

    /// <summary>Publishes the written buffer and hands the producer a free one.</summary>
    public void Publish()
    {
        int previous = Interlocked.Exchange(ref _middleState, _backIndex | FreshFlag);
        _backIndex = previous & IndexMask;
    }

    /// <summary>
    /// Returns the latest published frame (or the previous one again if nothing new was published).
    /// Only from the consumer thread; the returned buffer stays valid until the next call.
    /// </summary>
    public T ReadLatest()
    {
        if ((Volatile.Read(ref _middleState) & FreshFlag) != 0)
        {
            int previous = Interlocked.Exchange(ref _middleState, _frontIndex);
            _frontIndex = previous & IndexMask;
        }
        return _buffers[_frontIndex];
    }
}
