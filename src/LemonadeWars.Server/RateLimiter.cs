namespace LemonadeWars.Server;

/// <summary>Token bucket: bursts are fine, a sustained flood is not. Rate 0 disables.</summary>
internal sealed class RateLimiter
{
    private const double BurstSeconds = 3;

    private readonly double _ratePerSecond;
    private readonly double _burst;
    private double _tokens;
    private DateTime _lastRefillUtc = DateTime.UtcNow;

    public RateLimiter(double ratePerSecond)
    {
        _ratePerSecond = ratePerSecond;
        _burst = ratePerSecond * BurstSeconds;
        _tokens = _burst;
    }

    public bool TryTake()
    {
        if (_ratePerSecond <= 0)
        {
            return true;
        }
        var now = DateTime.UtcNow;
        _tokens = Math.Min(_burst,
            _tokens + (now - _lastRefillUtc).TotalSeconds * _ratePerSecond);
        _lastRefillUtc = now;
        if (_tokens < 1)
        {
            return false;
        }
        _tokens -= 1;
        return true;
    }
}
