namespace Lemon.Core.Wheel;

/// <summary>
/// Recognizes a quick back-and-forth "shake" of the mouse. Feed it every pointer position while the
/// button is held; it reports true once enough long-enough direction changes happen within the time window.
/// Allocation-free per call because it runs on every mouse move system-wide.
/// </summary>
/// <param name="minStroke">How far (px) each swing must travel to count.</param>
/// <param name="reversals">How many direction changes make a shake.</param>
/// <param name="windowMs">How quickly those direction changes must happen.</param>
/// <param name="jitter">Movement (px) against the current direction that's ignored as hand tremor.</param>
public sealed class ShakeDetector(int minStroke = 40, int reversals = 4, uint windowMs = 900, int jitter = 10)
{
    private readonly int _minStroke = minStroke;
    private readonly uint _windowMs = windowMs;
    private readonly int _jitter = jitter;
    private readonly Axis _x = new(reversals);
    private readonly Axis _y = new(reversals);
    private bool _started;

    public void Reset()
    {
        _started = false;
        _x.Clear();
        _y.Clear();
    }

    /// <returns>True when this move completes a shake.</returns>
    public bool Add(int x, int y, uint timeMs)
    {
        if (!_started)
        {
            _x.Begin(x);
            _y.Begin(y);
            _started = true;
            return false;
        }
        // Evaluate both so each axis keeps its state current.
        var shakeX = _x.Add(x, timeMs, this);
        var shakeY = _y.Add(y, timeMs, this);
        return shakeX || shakeY;
    }

    private sealed class Axis(int required)
    {
        private readonly uint[] _times = new uint[Math.Max(1, required)];
        private int _next;
        private int _filled;
        private int _strokeStart;
        private int _extreme;
        private int _direction;

        public void Begin(int position)
        {
            Clear();
            _strokeStart = _extreme = position;
        }

        public void Clear()
        {
            _next = _filled = _direction = 0;
        }

        public bool Add(int position, uint time, ShakeDetector owner)
        {
            if (_direction == 0)
            {
                if (Math.Abs(position - _strokeStart) >= owner._jitter)
                {
                    _direction = Math.Sign(position - _strokeStart);
                    _extreme = position;
                }
                return false;
            }

            if ((position - _extreme) * _direction > 0)
            {
                _extreme = position;
                return false;
            }
            if (Math.Abs(_extreme - position) < owner._jitter)
                return false;

            // Direction changed. Only long swings count; a short one means the hand wasn't shaking.
            if (Math.Abs(_extreme - _strokeStart) >= owner._minStroke)
            {
                _times[_next] = time;
                _next = (_next + 1) % _times.Length;
                _filled = Math.Min(_filled + 1, _times.Length);
            }
            else
            {
                _filled = 0;
            }

            _strokeStart = _extreme;
            _extreme = position;
            _direction = -_direction;

            // _times[_next] is now the oldest of the last `required` reversals.
            return _filled == _times.Length && unchecked(time - _times[_next]) <= owner._windowMs;
        }
    }
}
