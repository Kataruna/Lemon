namespace Lemon.Core.Tests.Wheel;

public sealed class KeyHoldTests
{
    [Fact]
    public void Opens_once_after_the_delay_and_reports_the_release()
    {
        var hold = new KeyHold();
        Assert.True(hold.Down(isWheelKey: true, now: 0));
        Assert.False(hold.Down(isWheelKey: true, now: 300)); // auto-repeat
        Assert.False(hold.Due(600));
        Assert.True(hold.Due(700));
        Assert.False(hold.Due(900));
        Assert.True(hold.IsHeld);
        Assert.True(hold.Up());
        Assert.False(hold.IsHeld);
    }

    [Fact]
    public void Another_key_or_a_click_in_between_means_it_was_a_modifier()
    {
        var hold = new KeyHold();
        hold.Down(true, 0);
        hold.Down(isWheelKey: false, now: 100); // Shift+arrow
        Assert.False(hold.Due(800));
        Assert.False(hold.Up());

        hold.Down(true, 1000);
        hold.Spoil(); // Shift+click
        Assert.False(hold.Due(2000));
        Assert.False(hold.Up());

        // The next clean hold works again.
        hold.Down(true, 3000);
        Assert.True(hold.Due(3700));
    }

    [Fact]
    public void Keys_pressed_while_the_wheel_is_up_dont_close_it()
    {
        var hold = new KeyHold();
        hold.Down(true, 0);
        hold.Due(700);
        hold.Down(isWheelKey: false, now: 800); // Tab to turn the ring
        Assert.True(hold.IsHeld);
        Assert.True(hold.Up());
    }
}
