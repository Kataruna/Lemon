namespace Lemon.Core.Wheel;

/// <summary>
/// The wheel key pressed again once the wheel is up over selected files: a tap enters what's lit, a hold of
/// <see cref="KeyHold.Delay"/> closes the wheel. A press with another key or a click in it (Alt+Tab) is neither.
/// The press that opened the wheel never comes here.
/// </summary>
public sealed class KeyTap
{
    private long _since = -1;
    private bool _spoiled;

    /// <summary>The key went down. True on the press itself (not its auto-repeats), so the caller can check back for a hold.</summary>
    public bool Down(long now)
    {
        if (_since >= 0)
            return false;
        (_since, _spoiled) = (now, false);
        return true;
    }

    /// <summary>Another key or a click while the key is down.</summary>
    public void Spoil()
    {
        if (_since >= 0)
            _spoiled = true;
    }

    /// <summary>Whether the key has been held long enough to close the wheel; true only once per press.</summary>
    public bool Due(long now)
    {
        if (_since < 0 || _spoiled || now - _since < (long)KeyHold.Delay.TotalMilliseconds)
            return false;
        _spoiled = true;
        return true;
    }

    /// <summary>The key came up. True when it was a tap.</summary>
    public bool Up()
    {
        var tap = _since >= 0 && !_spoiled;
        _since = -1;
        return tap;
    }
}
