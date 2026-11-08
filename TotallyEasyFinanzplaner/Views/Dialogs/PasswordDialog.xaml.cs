using System.Windows;
using System.Windows.Input;
using TotallyEasyFinanzplaner.Services;

namespace TotallyEasyFinanzplaner;

/// <summary>Passwortabfrage zum Entsperren sowie zum Festlegen, Ändern und Entfernen des Passworts.</summary>
public partial class PasswordDialog : Window
{
    private enum Mode { Unlock, Set, Change, Remove }

    private readonly Mode _mode;
    private Func<string, string?>? _checkCurrent;

    /// <summary>Das neu festgelegte Passwort (bei Festlegen/Ändern).</summary>
    public string NewPassword => NewFields.Password;

    /// <summary>Beim Entsperren auf "Anderen Plan öffnen…" geklickt.</summary>
    public bool WantsOtherPlan { get; private set; }

    private const string NoRecovery =
        "Der Plan wird mit AES-256 verschlüsselt. Ohne das Passwort lässt er sich nicht mehr öffnen – " +
        "es gibt keine Wiederherstellung. Bitte gut merken oder in einem Passwort-Manager ablegen.";

    private PasswordDialog(Mode mode, Window? owner)
    {
        InitializeComponent();
        _mode = mode;
        if (owner is { IsVisible: true }) Owner = owner;
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
        }

        CurrentPanel.Visibility = mode == Mode.Set ? Visibility.Collapsed : Visibility.Visible;
        NewFields.Visibility = mode is Mode.Set or Mode.Change ? Visibility.Visible : Visibility.Collapsed;
        NewFields.Margin = mode == Mode.Change ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        Loaded += (_, _) =>
        {
            if (mode == Mode.Set) NewFields.FocusFirst();
            else CurrentBox.Focus();
        };
    }

    /// <param name="tryUnlock">Versucht zu öffnen; gibt eine Fehlermeldung zurück oder null bei Erfolg.</param>
    public static PasswordDialog Unlock(Window? owner, string planName, string path, Func<string, string?> tryUnlock, bool allowOther)
    {
        var d = new PasswordDialog(Mode.Unlock, owner)
        {
            Title = "Finanzplan entsperren",
            _checkCurrent = tryUnlock,
        };
        d.HeaderText.Text = $"„{planName}“ entsperren";
        d.SubText.Text = "Dieser Finanzplan ist verschlüsselt. Bitte das Passwort eingeben.";
        d.OkButton.Content = "Entsperren";
        d.InfoIcon.Text = "";
        d.InfoText.Text = path;
        d.InfoText.SetResourceReference(ForegroundProperty, "MutedBrush");
        d.InfoText.TextTrimming = TextTrimming.CharacterEllipsis;
        d.InfoText.TextWrapping = TextWrapping.NoWrap;
        d.InfoText.ToolTip = path;
        d.InfoBox.Background = System.Windows.Media.Brushes.Transparent;
        d.InfoBox.Padding = new Thickness(0);
        d.InfoIcon.SetResourceReference(ForegroundProperty, "MutedBrush");
        d.OtherButton.Visibility = allowOther ? Visibility.Visible : Visibility.Collapsed;
        return d;
    }

    public static PasswordDialog SetPassword(Window? owner, string planName)
    {
        var d = new PasswordDialog(Mode.Set, owner) { Title = "Passwort festlegen" };
        d.HeaderText.Text = "Finanzplan verschlüsseln";
        d.SubText.Text = $"Lege ein Passwort für „{planName}“ fest.";
        d.OkButton.Content = "Verschlüsseln";
        d.InfoText.Text = NoRecovery;
        return d;
    }

    public static PasswordDialog ChangePassword(Window? owner, string planName, PlanKey current)
    {
        var d = new PasswordDialog(Mode.Change, owner)
        {
            Title = "Passwort ändern",
            _checkCurrent = pw => current.Matches(pw) ? null : "Das aktuelle Passwort ist falsch.",
        };
        d.HeaderText.Text = "Passwort ändern";
        d.SubText.Text = $"Neues Passwort für „{planName}“.";
        d.CurrentLabel.Text = "Aktuelles Passwort";
        d.OkButton.Content = "Ändern";
        d.InfoText.Text = "Der Plan wird sofort mit dem neuen Passwort neu verschlüsselt. Das alte Passwort gilt danach nicht mehr.";
        return d;
    }

    public static PasswordDialog RemovePassword(Window? owner, string planName, PlanKey current)
    {
        var d = new PasswordDialog(Mode.Remove, owner)
        {
            Title = "Verschlüsselung entfernen",
            _checkCurrent = pw => current.Matches(pw) ? null : "Das Passwort ist falsch.",
        };
        d.HeaderText.Text = "Verschlüsselung entfernen";
        d.HeaderIcon.Text = "";
        d.SubText.Text = $"Zur Bestätigung das Passwort von „{planName}“ eingeben.";
        d.OkButton.Content = "Entfernen";
        d.InfoIcon.Text = "";
        d.InfoIcon.SetResourceReference(ForegroundProperty, "OneTimeBrush");
        d.InfoBox.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "OneTimeSoftBrush");
        d.InfoText.Text = "Der Plan wird danach unverschlüsselt gespeichert. Jeder mit Zugriff auf die Datei kann ihn lesen.";
        return d;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>Prüfung läuft im Hintergrund – solange keine Eingaben und kein Schließen.</summary>
    private bool _busy;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_busy) e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (_busy) e.Handled = true;
        base.OnPreviewMouseDown(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_busy) e.Cancel = true; // sonst käme das Ergebnis für ein bereits geschlossenes Fenster zurück
        base.OnClosing(e);
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        ErrorText.Visibility = Visibility.Collapsed;

        if (_mode != Mode.Set)
        {
            if (CurrentBox.Password.Length == 0)
            {
                ShowError("Bitte das Passwort eingeben.");
                CurrentBox.Focus();
                return;
            }
        }
        if ((_mode is Mode.Set or Mode.Change) && NewFields.Validate() is { } invalid)
        {
            ShowError(invalid);
            return;
        }

        if (_checkCurrent is not null)
        {
            // Schlüsselableitung dauert absichtlich einen Moment (Schutz gegen Durchprobieren) –
            // im Hintergrund rechnen, damit das Fenster nicht einfriert
            string? error;
            var password = CurrentBox.Password;
            var check = _checkCurrent;
            _busy = true;
            Mouse.OverrideCursor = Cursors.Wait;
            OkButton.IsEnabled = false;
            try { error = await Task.Run(() => check(password)); }
            finally
            {
                _busy = false;
                Mouse.OverrideCursor = null;
                OkButton.IsEnabled = true;
            }
            if (error is not null)
            {
                ShowError(error);
                CurrentBox.Focus();
                CurrentBox.SelectAll();
                return;
            }
        }

        if (_mode == Mode.Change && NewFields.Password == CurrentBox.Password)
        {
            ShowError("Das neue Passwort ist dasselbe wie das aktuelle.");
            return;
        }

        DialogResult = true;
    }

    private void Other_Click(object sender, RoutedEventArgs e)
    {
        WantsOtherPlan = true;
        DialogResult = false;
    }

    private void CurrentBox_KeyUp(object sender, RoutedEventArgs e) =>
        CapsWarning.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;
}
