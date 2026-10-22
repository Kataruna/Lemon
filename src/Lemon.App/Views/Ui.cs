using Microsoft.UI.Xaml;

namespace Lemon.App.Views;

/// <summary>Small helpers for x:Bind function bindings.</summary>
public static class Ui
{
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfAnyText(string? first, string? second) => Visible(HasText(first) || HasText(second));

    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    public static bool Not(bool value) => !value;
}
