using Microsoft.UI.Xaml.Controls;

namespace Lemon.App.Views;

public sealed partial class PresetsPage : Page
{
    public PresetsPage()
    {
        InitializeComponent();
    }

    public PresetsViewModel ViewModel { get; } = new(App.Presets);
}
