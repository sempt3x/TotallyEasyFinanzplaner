using System.Media;
using System.Windows;
using System.Windows.Controls;

namespace TotallyEasyFinanzplaner;

/// <summary>
/// Meldungsfenster im Design der App (folgt Hell/Dunkel) als Ersatz für <see cref="MessageBox"/>.
/// Gleiche Parameter und Rückgabewerte wie MessageBox.Show.
/// </summary>
public partial class MessageDialog : Window
{
    private MessageBoxResult _result;

    private MessageDialog(string text, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        Title = HeaderText.Text = title;
        MessageText.Text = text;
        SetIcon(image);
        AddButtons(buttons);

        // Schließen über das X entspricht "Abbrechen" bzw. "Nein"
        _result = buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel,
        };
    }

    /// <summary>Zeigt eine Meldung an – Ersatz für <c>MessageBox.Show(owner, text, title, buttons, image)</c>.</summary>
    public static MessageBoxResult Show(Window? owner, string text, string title,
                                        MessageBoxButton buttons = MessageBoxButton.OK,
                                        MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new MessageDialog(text, title, buttons, image);
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                  ?? Application.Current?.MainWindow;
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
        }

        PlaySound(image);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void SetIcon(MessageBoxImage image)
    {
        // MessageBoxImage hat doppelte Werte (Error = Hand = Stop, Warning = Exclamation, Information = Asterisk)
        var (glyph, fg, bg) = image switch
        {
            MessageBoxImage.Error => ("", "ExpenseBrush", "ExpenseSoftBrush"),
            MessageBoxImage.Warning => ("", "OneTimeBrush", "OneTimeSoftBrush"),
            MessageBoxImage.Question => ("", "AccentBrush", "AccentSoftBrush"),
            MessageBoxImage.Information => ("", "AccentBrush", "AccentSoftBrush"),
            _ => ((string?)null, "", ""),
        };
        if (glyph is null)
        {
            IconBorder.Visibility = Visibility.Collapsed;
            return;
        }
        IconGlyph.Text = glyph;
        IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, fg);
        IconBorder.SetResourceReference(Border.BackgroundProperty, bg);
    }

    private void AddButtons(MessageBoxButton buttons)
    {
        var specs = buttons switch
        {
            MessageBoxButton.OKCancel => new[] { (MessageBoxResult.OK, "OK"), (MessageBoxResult.Cancel, "Abbrechen") },
            MessageBoxButton.YesNo => [(MessageBoxResult.Yes, "Ja"), (MessageBoxResult.No, "Nein")],
            MessageBoxButton.YesNoCancel => [(MessageBoxResult.Yes, "Ja"), (MessageBoxResult.No, "Nein"), (MessageBoxResult.Cancel, "Abbrechen")],
            _ => [(MessageBoxResult.OK, "OK")],
        };

        // Reihenfolge wie bei Windows-Meldungen: Hauptaktion (Ja/OK) zuerst, hervorgehoben
        for (int i = 0; i < specs.Length; i++)
        {
            var (result, text) = specs[i];
            bool primary = i == 0;
            var button = new Button
            {
                Content = text,
                MinWidth = 96,
                Margin = new Thickness(primary ? 0 : 8, 0, 0, 0),
                IsDefault = primary,
                // Esc: "Abbrechen", sonst "Nein" bei Ja/Nein, sonst "OK" bei reiner Meldung
                IsCancel = result == MessageBoxResult.Cancel
                           || (result == MessageBoxResult.No && buttons == MessageBoxButton.YesNo)
                           || buttons == MessageBoxButton.OK,
            };
            if (primary) button.Style = (Style)FindResource("PrimaryButton");
            button.Click += (_, _) => { _result = result; DialogResult = true; };
            ButtonPanel.Children.Add(button);
        }

        Loaded += (_, _) => ButtonPanel.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }

    private static void PlaySound(MessageBoxImage image)
    {
        switch (image)
        {
            case MessageBoxImage.Error: SystemSounds.Hand.Play(); break;
            case MessageBoxImage.Warning: SystemSounds.Exclamation.Play(); break;
            case MessageBoxImage.Question: SystemSounds.Question.Play(); break;
            case MessageBoxImage.Information: SystemSounds.Asterisk.Play(); break;
        }
    }
}
