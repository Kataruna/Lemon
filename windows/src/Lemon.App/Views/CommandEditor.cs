using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace Lemon.App.Views;

/// <summary>The dialog for making or changing a command on the Commands ring.</summary>
internal static class CommandEditor
{
    public static async Task<WheelCommand?> EditAsync(XamlRoot root, WheelCommand? command)
    {
        var draft = new WheelCommand();
        if (command is not null)
        {
            draft.Id = command.Id;
            draft.Name = command.Name;
            draft.Glyph = command.Glyph;
            draft.RunsOn = command.RunsOn;
            draft.Types = command.Types;
            draft.Line = command.Line;
        }

        var name = new TextBox { Header = "Name", Text = draft.Name, PlaceholderText = "e.g. Open in VS Code" };
        var icon = new ComboBox
        {
            Header = "Icon",
            ItemsSource = WheelCommand.Glyphs.Select(g => new FontIcon { Glyph = g, FontSize = 16 }).ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(WheelCommand.Glyphs, draft.Glyph)),
        };
        var runsOn = new RadioButtons
        {
            Header = "Runs on",
            MaxColumns = 3,
            ItemsSource = Enum.GetNames<RunsOn>(),
            SelectedIndex = (int)draft.RunsOn,
        };
        var types = new TextBox { Header = "File types", Text = draft.Types, PlaceholderText = "pdf, png (empty: any file)" };
        var insert = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var footer = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = command is null ? "New command" : "Edit command",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        var line = ChipBox(draft.Line, text =>
        {
            draft.Line = text;
            Refresh();
        });
        dialog.Content = new ScrollViewer
        {
            MaxHeight = 520,
            Content = new StackPanel { Spacing = 12, MinWidth = 420, Children = { name, icon, runsOn, types, line, insert, footer } },
        };

        void Refresh()
        {
            draft.RunsOn = (RunsOn)Math.Max(0, runsOn.SelectedIndex);
            types.Visibility = draft.RunsOn == RunsOn.Files ? Visibility.Visible : Visibility.Collapsed;
            footer.Text = draft.RunsOn switch
            {
                RunsOn.Files => "File is the dragged file; the command runs once for each, in the file's folder.",
                RunsOn.Folders => "Folder is the dragged folder, Files in Folder the files directly inside it; the command runs once for each folder, in it.",
                _ => "Link is the dragged link; the command runs once for each, in your user folder.",
            } + " Chips are quoted for you: don't put quotes around them. Runs in PowerShell. If it fails, Lemon shows what it printed.";
            dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0 && draft.Line.Trim().Length > 0;
        }

        void ShowInsertButtons()
        {
            insert.Children.Clear();
            insert.Children.Add(new TextBlock { Text = "Insert", VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            foreach (var token in CommandLine.Offered((RunsOn)Math.Max(0, runsOn.SelectedIndex)))
            {
                var button = new Button
                {
                    Content = "+ " + token.Label(),
                    FontSize = 12,
                    Padding = new Thickness(10, 3, 10, 4),
                    CornerRadius = new CornerRadius(12),
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    Background = new SolidColorBrush(ColorOf(token)),
                    BorderThickness = new Thickness(0),
                };
                button.Click += (_, _) =>
                {
                    // Where the cursor is (the box keeps its selection while the button has focus).
                    line.Document.Selection.TypeText(token.Text());
                    line.Focus(FocusState.Programmatic);
                };
                insert.Children.Add(button);
            }
        }

        runsOn.SelectionChanged += (_, _) =>
        {
            Refresh();
            ShowInsertButtons();
        };
        name.TextChanged += (_, _) => Refresh();
        Refresh();
        ShowInsertButtons();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;
        draft.Name = name.Text.Trim();
        draft.Glyph = WheelCommand.Glyphs[Math.Max(0, icon.SelectedIndex)];
        draft.Types = draft.RunsOn == RunsOn.Files ? types.Text.Trim() : "";
        draft.Line = draft.Line.Trim();
        return draft;
    }

    private static Color ColorOf(CommandToken token) => token switch
    {
        CommandToken.File => Color.FromArgb(255, 0, 120, 212),
        CommandToken.Folder => Color.FromArgb(255, 202, 80, 16),
        CommandToken.Files => Color.FromArgb(255, 136, 23, 152),
        _ => Color.FromArgb(255, 16, 124, 16),
    };

    /// <summary>
    /// A box for the command line where each placeholder ({folder}, {link}…) shows as a colored chip, typed or pasted as
    /// text. Copy and paste carry plain text, so chips travel as their placeholders.
    /// </summary>
    private static RichEditBox ChipBox(string text, Action<string> changed)
    {
        var box = new RichEditBox
        {
            Header = "Command",
            MinHeight = 72,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            // A shell line: no spelling fixes, word suggestions or bold / italic shortcuts.
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            DisabledFormattingAccelerators = DisabledFormattingAccelerators.All,
            ClipboardCopyFormat = RichEditClipboardFormat.PlainText,
        };
        box.Document.SetText(TextSetOptions.None, text);
        var painted = (string?)null;

        void Paint()
        {
            box.Document.GetText(TextGetOptions.None, out var raw);
            // The box always ends in a paragraph mark, and writes line breaks as \r.
            raw = raw.EndsWith('\r') ? raw[..^1] : raw;
            // Coloring the chips raises TextChanged too.
            if (raw == painted)
                return;
            painted = raw;
            var all = box.Document.GetRange(0, TextConstants.MaxUnitCount);
            all.CharacterFormat.BackgroundColor = Microsoft.UI.Colors.Transparent;
            all.CharacterFormat.ForegroundColor = ((SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"]).Color;
            all.CharacterFormat.Bold = FormatEffect.Off;
            var at = 0;
            foreach (var (piece, token) in CommandLine.Tokenize(raw))
            {
                if (token is { } t)
                {
                    var chip = box.Document.GetRange(at, at + piece.Length).CharacterFormat;
                    chip.BackgroundColor = ColorOf(t);
                    chip.ForegroundColor = Microsoft.UI.Colors.White;
                    chip.Bold = FormatEffect.On;
                }
                at += piece.Length;
            }
            changed(raw.Replace('\r', '\n'));
        }

        box.TextChanged += (_, _) => Paint();
        box.Paste += async (_, e) =>
        {
            // As plain text, so nothing pasted brings its own colors and fonts along.
            e.Handled = true;
            try
            {
                var content = Clipboard.GetContent();
                if (!content.Contains(StandardDataFormats.Text))
                    return;
                var pasted = await content.GetTextAsync();
                box.Document.Selection.SetText(TextSetOptions.None, pasted);
                box.Document.Selection.Collapse(false);
            }
            catch (Exception)
            {
                // The clipboard was busy or held something unreadable: nothing pasted.
            }
        };
        Paint();
        return box;
    }
}
