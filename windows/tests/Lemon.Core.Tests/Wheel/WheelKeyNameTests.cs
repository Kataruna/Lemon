namespace Lemon.Core.Tests.Wheel;

public sealed class WheelKeyNameTests
{
    [Theory]
    [InlineData("Menu", "Menu", "", "Alt")]
    [InlineData("RightMenu", "Menu", "Right", "Right Alt")]
    [InlineData("LeftControl", "Control", "Left", "Left Ctrl")]
    [InlineData("RightShift", "Shift", "Right", "Right Shift")]
    public void Keys_on_both_sides(string key, string name, string side, string shown)
    {
        Assert.Equal(name, WheelKeyName.Base(key));
        Assert.Equal(side, WheelKeyName.Side(key));
        Assert.Equal(shown, WheelKeyName.Display(key));
        Assert.Equal(key, WheelKeyName.WithSide(name, side));
    }

    [Theory]
    [InlineData("Left")] // the arrow key
    [InlineData("Right")]
    [InlineData("Space")]
    public void Other_keys_have_no_side(string key)
    {
        Assert.Null(WheelKeyName.Base(key));
        Assert.Equal("", WheelKeyName.Side(key));
        Assert.Equal(key, WheelKeyName.Display(key));
        Assert.Equal(key, WheelKeyName.WithSide(key, "Left"));
    }
}
