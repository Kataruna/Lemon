using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lemon.App.Views;

public sealed partial class PresetsPage : Page
{
    private int _category;

    public PresetsPage()
    {
        InitializeComponent();
        PresetGroups.Source = ViewModel.Groups;
        ShowCategoryRail();
    }

    public PresetsViewModel ViewModel { get; } = new(App.Presets);

    private void OnToggleCategories(object sender, RoutedEventArgs e)
    {
        App.Settings.PresetCategoriesExpanded = !App.Settings.PresetCategoriesExpanded;
        App.Settings.Save();
        ShowCategoryRail();
    }

    private void ShowCategoryRail() => CategoryRail.Width = App.Settings.PresetCategoriesExpanded ? 170 : 50;

    /// <summary>Ctrl+click clears a list's selection; a category stays lit. Not from inside this event, which ignores it.</summary>
    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        var list = (ListView)sender;
        if (list.SelectedIndex >= 0)
            _category = list.SelectedIndex;
        else
            DispatcherQueue.TryEnqueue(() => list.SelectedIndex = _category);
    }
}
