using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lemon.App.Views;

/// <summary>
/// Settings for the conversion wheel: its look, which rings it has, where it opens, and what's on the Convert ring.
/// Changes apply the next time the wheel opens.
/// </summary>
public sealed partial class WheelSettingsPage : Page
{
    private const string CustomTheme = "Custom";

    /// <summary>Each kind of file, with a sample file name to ask which presets fit it.</summary>
    private static readonly (MediaKind Kind, string Name, string Sample)[] Kinds =
    [
        (MediaKind.Image, "Images", "sample.png"),
        (MediaKind.Audio, "Audio", "sample.wav"),
        (MediaKind.Video, "Video", "sample.mp4"),
        (MediaKind.Document, "Documents", "sample.docx"),
    ];

    private readonly DispatcherQueueTimer _saveTimer;
    // Controls raise change events while the XAML loads (a slider is pushed up to its minimum, say); those aren't edits.
    private bool _loading = true;

    private static WheelSettings S => App.Settings.Wheel;

    public WheelSettingsPage()
    {
        InitializeComponent();
        // Sliders and color pickers change dozens of times a second, so save a moment after the last change.
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => App.Settings.Save();
        Unloaded += (_, _) =>
        {
            if (!_saveTimer.IsRunning)
                return;
            _saveTimer.Stop();
            App.Settings.Save();
        };

        KindBox.ItemsSource = Kinds.Select(k => k.Name).ToList();
        KindBox.SelectedIndex = 0;
        BlurChoice.ItemsSource = Glass.Names;
        _loading = false;
        LoadLook();
        BuildRings();
        BuildCommands();
        BuildOpensOn();
        BuildSlots();
        RefreshPreview();
    }

    private void Changed()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        RefreshPreview();
    }

    /// <summary>Redraws the preview wheel: what files of the kind picked under "Convert ring" open on.</summary>
    private void RefreshPreview()
    {
        var (_, name, sample) = SelectedKind;
        PreviewBox.Child = WheelPreview.Render(SelectedKind.Kind, sample);
        PreviewCaption.Text = $"Dragging {name.ToLowerInvariant()} (pick another kind under Convert ring)";
    }

    // ---- Look ----

    private void LoadLook()
    {
        _loading = true;
        var themes = WheelTheme.BuiltIn.Select(t => t.Name).ToList();
        if (S.Theme == CustomTheme)
            themes.Add(CustomTheme);
        if (ThemeBox.ItemsSource is not List<string> shown || !shown.SequenceEqual(themes))
            ThemeBox.ItemsSource = themes;
        ThemeBox.SelectedItem = S.Theme;

        var accent = S.Accent is { } hex ? WheelLook.Parse(hex) : (Color)Application.Current.Resources["SystemAccentColor"];
        AccentSwatch.Background = new SolidColorBrush(accent);
        AccentPicker.Color = accent;
        AccentNote.Visibility = S.Accent is null ? Visibility.Visible : Visibility.Collapsed;

        BlurChoice.SelectedIndex = Math.Clamp(S.Blur, 0, Glass.Names.Length - 1);
        TintSlider.Value = S.Tint;
        OpacitySlider.Value = S.Opacity;
        if (CenterBox.Text != S.Center)
            CenterBox.Text = S.Center;

        TextColorSwitch.IsOn = S.TextColor is not null;
        TextColorButton.Visibility = S.TextColor is null ? Visibility.Collapsed : Visibility.Visible;
        if (S.TextColor is { } text)
        {
            TextColorSwatch.Background = new SolidColorBrush(WheelLook.Parse(text));
            TextColorPicker.Color = WheelLook.Parse(text);
        }
        _loading = false;
    }

    /// <summary>Any change to the look makes it a custom theme; writing back the same value doesn't count.</summary>
    private void EditLook(Func<bool> apply)
    {
        if (_loading || !apply())
            return;
        S.Theme = CustomTheme;
        Changed();
        LoadLook();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not string name || WheelTheme.BuiltIn.FirstOrDefault(t => t.Name == name) is not { } theme)
            return;
        theme.ApplyTo(S);
        Changed();
        LoadLook();
    }

    private void OnAccentChanged(ColorPicker sender, ColorChangedEventArgs args) => EditLook(() =>
    {
        var hex = WheelLook.ToHex(args.NewColor);
        if (S.Accent == hex)
            return false;
        S.Accent = hex;
        return true;
    });

    /// <summary>Blur belongs to the glass, not the theme, so changing it keeps the theme.</summary>
    private void OnBlurChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || BlurChoice.SelectedIndex < 0 || BlurChoice.SelectedIndex == S.Blur)
            return;
        S.Blur = BlurChoice.SelectedIndex;
        Changed();
    }

    private void OnTintChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => EditLook(() =>
    {
        if (Math.Abs(S.Tint - e.NewValue) < 0.001)
            return false;
        S.Tint = e.NewValue;
        return true;
    });

    private void OnOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => EditLook(() =>
    {
        if (Math.Abs(S.Opacity - e.NewValue) < 0.001)
            return false;
        S.Opacity = e.NewValue;
        return true;
    });

    private void OnCenterChanged(object sender, TextChangedEventArgs e) => EditLook(() =>
    {
        // One emoji: the last one typed or pasted wins, so a new one replaces the old. Empty means Lemon's lemon.
        var elements = StringInfo.GetTextElementEnumerator(CenterBox.Text.Trim());
        var last = "🍋";
        while (elements.MoveNext())
            last = elements.GetTextElement();
        if (S.Center == last)
            return false;
        S.Center = last;
        return true;
    });

    private void OnTextColorToggled(object sender, RoutedEventArgs e) => EditLook(() =>
    {
        var value = TextColorSwitch.IsOn ? S.TextColor ?? "#FFFFFF" : null;
        if (S.TextColor == value)
            return false;
        S.TextColor = value;
        return true;
    });

    private void OnTextColorChanged(ColorPicker sender, ColorChangedEventArgs args) => EditLook(() =>
    {
        var hex = WheelLook.ToHex(args.NewColor);
        if (S.TextColor is null || S.TextColor == hex)
            return false;
        S.TextColor = hex;
        return true;
    });

    // ---- Rings ----

    private void BuildRings()
    {
        var visibleBuiltIns = WheelRing.BuiltIns.Count(r => !S.HiddenRings.Contains(r.Id));
        BuiltInRingsPanel.Children.Clear();
        foreach (var ring in WheelRing.BuiltIns)
        {
            var on = !S.HiddenRings.Contains(ring.Id);
            var box = new CheckBox
            {
                Content = Row(ring.Glyph, ring.Name, ring.Detail),
                IsChecked = on,
                // Keep at least one ring: the last built-in one stays on unless there's a ring of your own.
                IsEnabled = !(on && visibleBuiltIns == 1 && S.Rings.Count == 0),
            };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true)
                    S.HiddenRings.Remove(ring.Id);
                else
                    S.HiddenRings.Add(ring.Id);
                Changed();
                BuildRings();
                BuildOpensOn();
            };
            BuiltInRingsPanel.Children.Add(box);
        }

        CustomRingsPanel.Children.Clear();
        foreach (var ring in S.Rings)
        {
            var edit = new Button { Content = "Edit" };
            edit.Click += async (_, _) => await EditRingAsync(ring);
            var delete = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                IsEnabled = !(visibleBuiltIns == 0 && S.Rings.Count == 1),
            };
            ToolTipService.SetToolTip(delete, "Delete ring");
            delete.Click += (_, _) =>
            {
                S.Rings.Remove(ring);
                foreach (var kind in S.OpensOn.Where(o => o.Value == ring.Id).Select(o => o.Key).ToList())
                    S.OpensOn.Remove(kind);
                Changed();
                BuildRings();
                BuildOpensOn();
            };
            var tools = ring.PresetIds.Count(id => App.Presets.Find(id) is not null);
            var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Row(ring.Glyph, ring.Name, tools == 1 ? "1 tool" : $"{tools} tools"));
            Grid.SetColumn(edit, 1);
            Grid.SetColumn(delete, 2);
            grid.Children.Add(edit);
            grid.Children.Add(delete);
            CustomRingsPanel.Children.Add(grid);
        }
        NewRingButton.IsEnabled = S.Rings.Count < WheelSettings.MaxCustomRings;
    }

    private static StackPanel Row(string glyph, string title, string detail) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new FontIcon { Glyph = glyph, FontSize = 16 },
            new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = detail, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
        },
    };

    private async void OnNewRing(object sender, RoutedEventArgs e) => await EditRingAsync(null);

    private async Task EditRingAsync(CustomRing? ring)
    {
        if (await RingEditor.EditAsync(XamlRoot, ring) is not { } edited)
            return;
        var index = ring is null ? -1 : S.Rings.IndexOf(ring);
        if (index >= 0)
            S.Rings[index] = edited;
        else
            S.Rings.Add(edited);
        Changed();
        BuildRings();
        BuildOpensOn();
    }

    // ---- Commands ----

    private void BuildCommands()
    {
        CommandsPanel.Children.Clear();
        for (var i = 0; i < S.Commands.Count; i++)
        {
            var (index, command) = (i, S.Commands[i]);
            var edit = new Button { Content = "Edit" };
            edit.Click += async (_, _) => await EditCommandAsync(command);
            var grid = new Grid { ColumnSpacing = 2, Padding = new Thickness(4, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var c = 0; c < 4; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Row(command.Glyph, command.Name, command.RunsOn.ToString()));
            grid.Children.Add(OrderedList.IconButton("","Move up", 1, index > 0, () => Swap(index - 1)));
            grid.Children.Add(OrderedList.IconButton("","Move down", 2, index < S.Commands.Count - 1, () => Swap(index)));
            Grid.SetColumn(edit, 3);
            grid.Children.Add(edit);
            grid.Children.Add(OrderedList.IconButton("","Delete command", 4, true, () =>
            {
                S.Commands.Remove(command);
                Changed();
                BuildCommands();
            }));
            CommandsPanel.Children.Add(grid);
        }

        void Swap(int first)
        {
            (S.Commands[first], S.Commands[first + 1]) = (S.Commands[first + 1], S.Commands[first]);
            Changed();
            BuildCommands();
        }
    }

    private async void OnNewCommand(object sender, RoutedEventArgs e) => await EditCommandAsync(null);

    private async Task EditCommandAsync(WheelCommand? command)
    {
        if (await CommandEditor.EditAsync(XamlRoot, command) is not { } edited)
            return;
        var index = command is null ? -1 : S.Commands.IndexOf(command);
        if (index >= 0)
            S.Commands[index] = edited;
        else
            S.Commands.Add(edited);
        Changed();
        BuildCommands();
    }

    // ---- Opens on ----

    private void BuildOpensOn()
    {
        _loading = true;
        HubSwitch.IsOn = S.OpenOnHub;
        BackChipSwitch.IsOn = S.BackChip;
        _loading = false;
        OpensOnPanel.Children.Clear();
        OpensOnPanel.Visibility = S.OpenOnHub ? Visibility.Collapsed : Visibility.Visible;
        var rings = WheelRing.All(S).Where(r => !S.HiddenRings.Contains(r.Id)).ToList();
        foreach (var (kind, name, _) in Kinds)
        {
            var box = new ComboBox
            {
                Header = name,
                MinWidth = 220,
                ItemsSource = rings.Select(r => r.Name).ToList(),
                SelectedIndex = Math.Max(0, rings.FindIndex(r => r.Id == S.OpensOn.GetValueOrDefault(kind))),
            };
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0)
                    return;
                S.OpensOn[kind] = rings[box.SelectedIndex].Id;
                Changed();
            };
            OpensOnPanel.Children.Add(box);
        }
    }

    private void OnHubToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.OpenOnHub = HubSwitch.IsOn;
        Changed();
        BuildOpensOn();
    }

    private void OnBackChipToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.BackChip = BackChipSwitch.IsOn;
        Changed();
    }

    // ---- Convert ring ----

    private (MediaKind Kind, string Name, string Sample) SelectedKind => Kinds[Math.Max(0, KindBox.SelectedIndex)];

    /// <summary>What can go on the Convert ring for this kind of file: the format presets that can be made from it.</summary>
    private static List<Preset> Candidates(string sample) =>
        App.Queue.PresetsFor(sample).Where(p => ConversionWheel.PageOf(p) == WheelPage.Convert).ToList();

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        BuildSlots();
        RefreshPreview();
    }

    private void BuildSlots()
    {
        var (kind, _, sample) = SelectedKind;
        var candidates = Candidates(sample);
        // Ids whose preset is gone (deleted) or no longer fits this kind are dropped.
        var slots = (S.ConvertRing.TryGetValue(kind, out var mine) ? mine : ConversionWheel.DefaultConvertRing(kind))
            .Where(id => candidates.Any(c => c.Id == id))
            .Take(ConversionWheel.MaxChoices)
            .ToList();

        SlotsPanel.Children.Clear();
        SlotsPanel.Children.Add(OrderedList.Build(slots, id => candidates.First(c => c.Id == id).Name, list =>
        {
            S.ConvertRing[kind] = list;
            Changed();
            BuildSlots();
        }));

        AddFormatMenu.Items.Clear();
        foreach (var preset in candidates.Where(c => !slots.Contains(c.Id)))
        {
            var item = new MenuFlyoutItem { Text = preset.Name };
            item.Click += (_, _) =>
            {
                S.ConvertRing[kind] = [.. slots, preset.Id];
                Changed();
                BuildSlots();
            };
            AddFormatMenu.Items.Add(item);
        }
        AddFormatButton.IsEnabled = slots.Count < ConversionWheel.MaxChoices && AddFormatMenu.Items.Count > 0;
        ResetSlotsButton.IsEnabled = S.ConvertRing.ContainsKey(kind);
    }

    private void OnResetSlots(object sender, RoutedEventArgs e)
    {
        S.ConvertRing.Remove(SelectedKind.Kind);
        Changed();
        BuildSlots();
    }
}

/// <summary>Preset names in wheel order, each with move up, move down and remove.</summary>
internal static class OrderedList
{
    public static StackPanel Build(IReadOnlyList<string> ids, Func<string, string> name, Action<List<string>> set)
    {
        var panel = new StackPanel();
        for (var i = 0; i < ids.Count; i++)
        {
            var index = i;
            var grid = new Grid { ColumnSpacing = 2, Padding = new Thickness(4, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var c = 0; c < 3; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = $"{i + 1}.  {name(ids[i])}", VerticalAlignment = VerticalAlignment.Center });
            grid.Children.Add(IconButton("", "Move up", 1, index > 0, () => Swap(index - 1)));
            grid.Children.Add(IconButton("", "Move down", 2, index < ids.Count - 1, () => Swap(index)));
            grid.Children.Add(IconButton("", "Remove", 3, true, () => set([.. ids.Where((_, j) => j != index)])));
            panel.Children.Add(grid);
        }
        return panel;

        void Swap(int first)
        {
            var list = ids.ToList();
            (list[first], list[first + 1]) = (list[first + 1], list[first]);
            set(list);
        }
    }

    public static Button IconButton(string glyph, string label, int column, bool enabled, Action click)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            IsEnabled = enabled,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        Grid.SetColumn(button, column);
        button.Click += (_, _) => click();
        return button;
    }
}

/// <summary>The dialog for making or changing a ring of the user's own.</summary>
internal static class RingEditor
{
    public static async Task<CustomRing?> EditAsync(XamlRoot root, CustomRing? ring)
    {
        var draft = new CustomRing();
        if (ring is not null)
        {
            draft.Id = ring.Id;
            draft.Name = ring.Name;
            draft.Glyph = ring.Glyph;
            draft.PresetIds = [.. ring.PresetIds.Where(id => App.Presets.Find(id) is not null)];
        }

        var name = new TextBox { Header = "Name", Text = draft.Name, PlaceholderText = "e.g. Web" };
        var icon = new ComboBox
        {
            Header = "Icon",
            ItemsSource = CustomRing.Glyphs.Select(g => new FontIcon { Glyph = g, FontSize = 16 }).ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(CustomRing.Glyphs, draft.Glyph)),
        };
        var tools = new StackPanel();
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        var add = new DropDownButton { Content = "Add tool", Flyout = menu };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = ring is null ? "New ring" : "Edit ring",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 480,
                Content = new StackPanel
                {
                    Spacing = 12,
                    MinWidth = 380,
                    Children =
                    {
                        name,
                        icon,
                        new TextBlock { Text = "Tools", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] },
                        tools,
                        add,
                        new TextBlock
                        {
                            Text = "Up to 8, clockwise from the top. Mix tools for different kinds of files: each file only sees the ones that work for it.",
                            TextWrapping = TextWrapping.Wrap,
                            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        },
                    },
                },
            },
        };

        void Refresh()
        {
            tools.Children.Clear();
            if (draft.PresetIds.Count == 0)
                tools.Children.Add(new TextBlock { Text = "Add the tools you want on this ring.", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            else
                tools.Children.Add(OrderedList.Build(draft.PresetIds, id => App.Presets.Find(id)!.Name, list =>
                {
                    draft.PresetIds = list;
                    Refresh();
                }));

            menu.Items.Clear();
            foreach (var (group, presets) in Groups())
            {
                var sub = new MenuFlyoutSubItem { Text = group };
                foreach (var preset in presets.Where(p => !draft.PresetIds.Contains(p.Id)))
                {
                    var item = new MenuFlyoutItem { Text = preset.Name };
                    item.Click += (_, _) =>
                    {
                        draft.PresetIds.Add(preset.Id);
                        Refresh();
                    };
                    sub.Items.Add(item);
                }
                if (sub.Items.Count > 0)
                    menu.Items.Add(sub);
            }
            add.IsEnabled = draft.PresetIds.Count < ConversionWheel.MaxChoices;
            dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0 && draft.PresetIds.Count > 0;
        }

        name.TextChanged += (_, _) => Refresh();
        Refresh();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;
        draft.Name = name.Text.Trim();
        draft.Glyph = CustomRing.Glyphs[Math.Max(0, icon.SelectedIndex)];
        return draft;
    }

    /// <summary>Every preset a ring can hold, grouped for the Add tool menu.</summary>
    private static IEnumerable<(string Group, List<Preset> Presets)> Groups()
    {
        var all = App.Presets.All;
        yield return ("Optimize", all.Where(p => p.IsOptimize).ToList());
        yield return ("Resize images", all.Where(p => p.IsResize).ToList());
        yield return ("Remove background", all.Where(p => p.RemoveBackground is not null).ToList());
        var formats = all.Where(p => ConversionWheel.PageOf(p) == WheelPage.Convert).ToList();
        foreach (var (kind, label) in new[] { (MediaKind.Image, "Image formats"), (MediaKind.Audio, "Audio formats"), (MediaKind.Video, "Video formats"), (MediaKind.Document, "Document formats") })
            yield return (label, formats.Where(p => p.TargetKind == kind).ToList());
    }
}
