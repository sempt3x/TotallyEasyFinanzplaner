using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TotallyEasyFinanzplaner;

/// <summary>Eingabe eines neuen Passworts mit Wiederholung und Stärkeanzeige.</summary>
public partial class NewPasswordFields : UserControl
{
    public const int MinLength = 8;

    public NewPasswordFields()
    {
        InitializeComponent();
    }

    public string Password => NewBox.Password;

    public string Label
    {
        get => NewLabel.Text;
        set => NewLabel.Text = value;
    }

    public void FocusFirst() => NewBox.Focus();

    public void Clear()
    {
        NewBox.Clear();
        ConfirmBox.Clear();
    }

    /// <summary>Gibt eine Fehlermeldung zurück oder null, wenn beide Eingaben passen.</summary>
    public string? Validate()
    {
        if (NewBox.Password.Length < MinLength)
        {
            NewBox.Focus();
            return $"Das Passwort muss mindestens {MinLength} Zeichen lang sein.";
        }
        if (NewBox.Password != ConfirmBox.Password)
        {
            ConfirmBox.Focus();
            ConfirmBox.SelectAll();
            return "Die beiden Passwörter stimmen nicht überein.";
        }
        return null;
    }

    /// <summary>Bewertung 0 (sehr schwach) bis 4 (stark) – grobe Schätzung aus Länge und Zeichenarten.</summary>
    public static int Strength(string pw)
    {
        if (pw.Length == 0) return 0;
        int classes = (pw.Any(char.IsLower) ? 1 : 0) + (pw.Any(char.IsUpper) ? 1 : 0)
                    + (pw.Any(char.IsDigit) ? 1 : 0) + (pw.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        int score = 0;
        if (pw.Length >= MinLength) score++;
        if (pw.Length >= 12) score++;
        if (classes >= 3) score++;
        if (pw.Length >= 16 || (pw.Length >= 12 && classes == 4)) score++;
        if (pw.Length < MinLength) score = Math.Min(score, 0);
        if (pw.Distinct().Count() <= 2) score = Math.Min(score, 1); // z. B. "aaaaaaaa"
        return score;
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        var pw = NewBox.Password;
        int score = Strength(pw);
        var (text, brush) = pw.Length == 0 ? ("–", "LineBrush")
            : pw.Length < MinLength ? ("Zu kurz", "ExpenseBrush")
            : score switch
            {
                <= 1 => ("Schwach", "ExpenseBrush"),
                2 => ("Mittel", "OneTimeBrush"),
                3 => ("Gut", "IncomeBrush"),
                _ => ("Stark", "IncomeBrush"),
            };
        StrengthText.Text = text;
        int filled = pw.Length == 0 ? 0 : Math.Max(1, score);
        for (int i = 0; i < StrengthBar.Children.Count; i++)
            ((Border)StrengthBar.Children[i]).SetResourceReference(Border.BackgroundProperty, i < filled ? brush : "LineBrush");

        if (ConfirmBox.Password.Length == 0)
            MatchText.Visibility = Visibility.Collapsed;
        else
        {
            bool match = ConfirmBox.Password == pw;
            MatchText.Text = match ? "✓ Passwörter stimmen überein" : "Passwörter stimmen nicht überein";
            MatchText.SetResourceReference(TextBlock.ForegroundProperty, match ? "IncomeBrush" : "ExpenseBrush");
            MatchText.Visibility = Visibility.Visible;
        }
    }

    private void Box_KeyUp(object sender, RoutedEventArgs e) =>
        CapsWarning.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;
}
