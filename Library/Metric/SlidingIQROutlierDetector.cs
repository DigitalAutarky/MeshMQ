namespace HackyMessage.Metric;

public sealed class SlidingIqrOutlierDetector(int windowSize = 128, int recalcInterval = 32)
{
    private readonly long[] _window = new long[windowSize];
    private readonly long[] _sortBuffer = new long[windowSize];
    private readonly object _syncLock = new();
    
    private int _index = 0;
    private int _count = 0;
    private int _sinceLastRecalc = 0;

    // Use volatile to allow thread-safe reading without a lock
    private volatile VolatileThresholds _thresholds = new(0, long.MaxValue);

    public bool IsOutlier(long size)
    {
        var current = _thresholds; 
        return size > current.Upper || size < current.Lower;
    }
    
    public bool IsBigOutlier(long size)
    {
        var current = _thresholds; 
        return size > current.Upper;
    }
    
    public bool IsSmallOutlier(long size)
    {
        var current = _thresholds; 
        return size < current.Lower;
    }

    public void Observe(long size)
    {
        lock (_syncLock)
        {
            _window[_index] = size;
            _index = (_index + 1) % _window.Length;
            if (_count < _window.Length) _count++;

            if (++_sinceLastRecalc >= recalcInterval && _count >= 20)
            {
                UpdateThresholds();
                _sinceLastRecalc = 0;
            }
        }
    }

    private void UpdateThresholds()
    {
        _window.AsSpan(0, _count).CopyTo(_sortBuffer);
        var sortSpan = _sortBuffer.AsSpan(0, _count);
        sortSpan.Sort();

        var q1 = sortSpan[(int)(_count * 0.25)];
        var q3 = sortSpan[(int)(_count * 0.75)];
        var iqr = q3 - q1;

        // Calculate both fences
        var upper = q3 + (long)(1.5 * iqr);
        var lower = Math.Max(0, q1 - (long)(1.5 * iqr));

        // Atomically update the thresholds
        _thresholds = new VolatileThresholds(lower, upper);
    }

    // Helper class to ensure atomic updates of multiple values
    private class VolatileThresholds(long l, long u)
    {
        public readonly long Lower = l;
        public readonly long Upper = u;
    }
}