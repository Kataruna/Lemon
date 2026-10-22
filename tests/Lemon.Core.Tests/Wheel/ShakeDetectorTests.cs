namespace Lemon.Core.Tests;

public class ShakeDetectorTests
{
    /// <summary>Moves back and forth horizontally: <paramref name="swings"/> swings of <paramref name="width"/> px, each taking <paramref name="msPerSwing"/>.</summary>
    private static bool Zigzag(ShakeDetector detector, int swings, int width, uint msPerSwing, int stepsPerSwing = 6)
    {
        uint t = 1000;
        var x = 500;
        var shook = detector.Add(x, 300, t);
        for (var s = 0; s < swings; s++)
        {
            var direction = s % 2 == 0 ? 1 : -1;
            for (var i = 0; i < stepsPerSwing; i++)
            {
                x += direction * width / stepsPerSwing;
                t += msPerSwing / (uint)stepsPerSwing;
                shook |= detector.Add(x, 300 + (i % 2), t);
            }
        }
        return shook;
    }

    [Fact]
    public void QuickShakeTriggers() => Assert.True(Zigzag(new ShakeDetector(), swings: 6, width: 80, msPerSwing: 90));

    [Fact]
    public void SlowBackAndForthDoesNot() => Assert.False(Zigzag(new ShakeDetector(), swings: 6, width: 80, msPerSwing: 500));

    [Fact]
    public void SmallWobbleDoesNot() => Assert.False(Zigzag(new ShakeDetector(), swings: 10, width: 20, msPerSwing: 60));

    [Fact]
    public void TooFewSwingsDoNot() => Assert.False(Zigzag(new ShakeDetector(), swings: 4, width: 80, msPerSwing: 90));

    [Fact]
    public void StraightDragDoesNot()
    {
        var detector = new ShakeDetector();
        var shook = false;
        for (var i = 0; i < 200; i++)
            shook |= detector.Add(100 + i * 5, 100 + i * 2, (uint)(i * 8));
        Assert.False(shook);
    }

    [Fact]
    public void VerticalShakeTriggers()
    {
        var detector = new ShakeDetector();
        uint t = 0;
        var y = 400;
        var shook = detector.Add(300, y, t);
        for (var s = 0; s < 6; s++)
            for (var i = 0; i < 5; i++)
                shook |= detector.Add(300, y += (s % 2 == 0 ? 16 : -16), t += 18);
        Assert.True(shook);
    }

    [Fact]
    public void ResetForgetsPartialShake()
    {
        var detector = new ShakeDetector();
        Zigzag(detector, swings: 4, width: 80, msPerSwing: 90);
        detector.Reset();
        Assert.False(Zigzag(detector, swings: 3, width: 80, msPerSwing: 90));
    }
}
