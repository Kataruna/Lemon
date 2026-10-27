namespace Lemon.Core.Tests.Wheel;

public sealed class KeyTapTests
{
    [Fact]
    public void A_tap_enters_and_a_hold_closes()
    {
        var tap = new KeyTap();
        Assert.True(tap.Down(0));
        Assert.False(tap.Down(100)); // auto-repeat
        Assert.True(tap.Up());

        tap.Down(1000);
        Assert.False(tap.Due(1600));
        Assert.True(tap.Due(1700));
        Assert.False(tap.Due(1800));
        Assert.False(tap.Up()); // letting go after the close isn't a tap
    }

    [Fact]
    public void Another_key_in_the_press_spoils_it()
    {
        var tap = new KeyTap();
        tap.Spoil(); // D on its own, no press going on
        tap.Down(0);
        Assert.True(tap.Up());

        tap.Down(1000);
        tap.Spoil(); // Alt+Tab
        Assert.False(tap.Due(2000));
        Assert.False(tap.Up());

        tap.Down(3000);
        Assert.True(tap.Up());
    }
}
