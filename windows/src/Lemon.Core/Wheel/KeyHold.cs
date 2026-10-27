namespace Lemon.Core.Wheel;

/// <summary>
/// The wheel key held on its own, without a drag, opens the wheel on the files selected in File Explorer. Any other key,
/// a mouse button or a scroll in the meantime means it was part of something else (Shift+click, Shift+arrows, typing a
/// capital), so the hold doesn't count.
/// </summary>
public sealed class KeyHold
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(700);

    private long _since;
    private bool _pressing;
    private bool _spoiled;

    /// <summary>The hold has opened the wheel and the key is still down.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>A key went down. True when it started a hold, so the caller should check back after <see cref="Delay"/>.</summary>
    public bool Down(bool isWheelKey, long now)
    {
        if (!isWheelKey)
        {
            Spoil();
            return false;
        }
        // Auto-repeats while it's held change nothing.
        if (_pressing)
            return false;
        (_pressing, _spoiled, _since) = (true, false, now);
        return true;
    }

    /// <summary>A mouse button, a scroll or a drag: the key is a modifier for that, not a hold.</summary>
    public void Spoil()
    {
        if (_pressing && !IsHeld)
            _spoiled = true;
    }

    /// <summary>Whether the hold is due now; true only once per hold.</summary>
    public bool Due(long now)
    {
        if (!_pressing || _spoiled || IsHeld || now - _since < (long)Delay.TotalMilliseconds)
            return false;
        IsHeld = true;
        return true;
    }

    /// <summary>The wheel key came up. True when the hold had opened the wheel.</summary>
    public bool Up()
    {
        var held = IsHeld;
        (_pressing, _spoiled, IsHeld) = (false, false, false);
        return held;
    }
}
