using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TotallyEasyFinanzplaner.Models;
using TotallyEasyFinanzplaner.Services;
using Microsoft.Win32;

namespace TotallyEasyFinanzplaner;

/// <summary>Mehrere Finanzpläne: Neu, Öffnen, Speichern unter, zuletzt verwendet, Verschlüsselung.</summary>
public partial class MainWindow
{
    // ------------------------------------------------------------------ Plan laden / anzeigen

    private void LoadPlan(PlanDocument doc)
    {
        _doc = doc;
        _selected = Today;
        _month = FirstOfMonth(Today);

        _suppressEvents = true;
        StartDatePicker.SelectedDate = ToDateTime(_data.StartDate);
        StartBalanceBox.Text = Money.Plain(_data.StartBalance);
        _suppressEvents = false;

        AppSettings.Current.RememberFile(doc.FilePath);
        UpdatePlanUi();
        StatusSaved.Text = $"Geöffnet um {DateTime.Now:HH:mm}";
        RefreshAll();
    }

    /// <summary>Wechselt zu einem anderen (bereits geöffneten) Plan.</summary>
    private void SwitchTo(PlanDocument doc)
    {
        LoadPlan(doc);
        ShowPage(NavDashboard);
        ShowLoadNotices();
    }

    private void UpdatePlanUi()
    {
        Title = $"{_doc.Name} – Totally Easy Finanzplaner";
        PlanNameText.Text = _doc.Name;
        PlanButton.ToolTip = _doc.FilePath;
        PlanIcon.Text = _doc.IsEncrypted ? "" : "";
        PlanStateText.Text = _doc.IsEncrypted ? "Verschlüsselt (AES-256)" : "Nicht verschlüsselt";
        StatusLockIcon.Text = _doc.IsEncrypted ? "" : "";
        StatusLock.Text = _doc.IsEncrypted ? "AES-256 verschlüsselt" : "Unverschlüsselt";
        StatusPath.Text = _doc.FilePath;
    }

    /// <summary>Noch nicht übernommene Eingabe im Startsaldo-Feld speichern (z. B. vor dem Planwechsel).</summary>
    private void FlushPendingEdits()
    {
        if (Money.TryParse(StartBalanceBox.Text, out var value) && value != _data.StartBalance)
        {
            _data.StartBalance = value;
            Save();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Während der Schlüssel noch abgeleitet wird, nicht schließen – sonst ginge die Änderung verloren
        if (_busy)
        {
            e.Cancel = true;
            return;
        }
        FlushPendingEdits();
    }

    // ------------------------------------------------------------------ Dateimenü

    private void PlanButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PlanButton, Placement = PlacementMode.Bottom, MinWidth = 310 };

        menu.Items.Add(Item("Neuer Finanzplan…", "", "Strg+N", NewPlan));
        menu.Items.Add(Item("Öffnen…", "", "Strg+O", OpenPlan));

        var recent = new MenuItem { Header = "Zuletzt geöffnet", Icon = Glyph("") };
        var others = AppSettings.Current.RecentFiles
            .Where(p => !string.Equals(p, _doc.FilePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var path in others)
        {
            bool exists = File.Exists(path) || DataStore.IsDefaultPlan(path);
            bool locked = exists && PlanCrypto.IsEncryptedFile(path);
            var item = Item(DataStore.DisplayName(path), locked ? "" : "", null, () => OpenPath(path));
            item.ToolTip = exists ? path : path + "\n(Datei nicht gefunden)";
            item.IsEnabled = exists;
            recent.Items.Add(item);
        }
        if (others.Count > 0)
        {
            recent.Items.Add(new Separator());
            recent.Items.Add(Item("Liste leeren", "", null, () =>
            {
                AppSettings.Current.RecentFiles.RemoveAll(p => !string.Equals(p, _doc.FilePath, StringComparison.OrdinalIgnoreCase));
                AppSettings.Current.Save();
            }));
        }
        else recent.Items.Add(new MenuItem { Header = "Keine weiteren Pläne", IsEnabled = false });
        menu.Items.Add(recent);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Speichern", "", "Strg+S", SaveNow));
        menu.Items.Add(Item("Speichern unter…", "", "Strg+Umschalt+S", SavePlanAs));

        menu.Items.Add(new Separator());
        if (_doc.IsEncrypted)
        {
            menu.Items.Add(Item("Passwort ändern…", "", null, ChangePassword));
            menu.Items.Add(Item("Verschlüsselung entfernen…", "", null, RemovePassword));
        }
        else menu.Items.Add(Item("Mit Passwort verschlüsseln…", "", null, SetPassword));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Im Explorer anzeigen", "", null, () => OpenFolder_Click(this, new RoutedEventArgs())));

        menu.IsOpen = true;
    }

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("IconFont"),
        FontSize = 14,
    };

    private static MenuItem Item(string header, string glyph, string? gesture, Action action)
    {
        var item = new MenuItem { Header = header, Icon = Glyph(glyph), InputGestureText = gesture ?? "" };
        // Erst nach dem Schließen des Menüs ausführen, damit Dialoge nicht hinter dem Menü erscheinen
        item.Click += (_, _) => Application.Current.Dispatcher.BeginInvoke(action);
        return item;
    }

    // ------------------------------------------------------------------ Neu / Öffnen / Speichern

    /// <summary>Gesetzt, solange ein Schlüssel im Hintergrund abgeleitet wird – dann sind Eingaben gesperrt.</summary>
    private bool _busy;

    /// <summary>
    /// Leitet den Schlüssel im Hintergrund ab (dauert absichtlich etwa eine halbe Sekunde), damit das Fenster
    /// nicht einfriert. Währenddessen werden Maus- und Tastatureingaben ignoriert, damit z. B. kein Planwechsel
    /// dazwischenkommt.
    /// </summary>
    private async Task<PlanKey> CreateKeyAsync(string password)
    {
        _busy = true;
        Mouse.OverrideCursor = Cursors.Wait;
        try { return await Task.Run(() => PlanCrypto.CreateKey(password)); }
        finally
        {
            Mouse.OverrideCursor = null;
            _busy = false;
        }
    }

    private void Window_PreviewMouseDownWhileBusy(object sender, MouseButtonEventArgs e)
    {
        if (_busy) e.Handled = true;
    }

    private async void NewPlan()
    {
        if (_busy) return;
        FlushPendingEdits();
        var current = Path.GetDirectoryName(_doc.FilePath);
        var folder = !DataStore.IsDefaultPlan(_doc.FilePath) && current is not null && Directory.Exists(current)
            ? current : DataStore.DocumentsFolder;

        var dialog = new NewPlanDialog(folder) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        try
        {
            PlanKey? key = dialog.Password is { } pw ? await CreateKeyAsync(pw) : null;
            var doc = DataStore.Create(dialog.FilePath, key);
            SwitchTo(doc);
            StatusSaved.Text = $"Neuer Plan angelegt um {DateTime.Now:HH:mm}";
            MessageDialog.Show(this,
                $"„{doc.Name}“ wurde angelegt{(doc.IsEncrypted ? " und ist mit AES-256 verschlüsselt" : "")}.\n\n" +
                "Lege oben rechts Stichtag und Startsaldo fest und erfasse dann unter \"Buchungen\" " +
                "Gehälter, Einnahmen und Ausgaben. Änderungen werden automatisch gespeichert.\n\n" +
                $"Datei: {doc.FilePath}",
                "Neuer Finanzplan", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Show(this, $"Der Plan konnte nicht angelegt werden:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenPlan()
    {
        FlushPendingEdits();
        if (PlanOpener.Browse(this, _doc.FilePath) is { } path) OpenPath(path);
    }

    private void OpenPath(string path)
    {
        // Während ein Schlüssel abgeleitet wird, kein Planwechsel (Drag & Drop umgeht die Eingabesperre) –
        // sonst bekäme der neu geöffnete Plan den Schlüssel des vorherigen
        if (_busy) return;
        FlushPendingEdits();
        if (string.Equals(Path.GetFullPath(path), _doc.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            StatusSaved.Text = $"„{_doc.Name}“ ist bereits geöffnet";
            return;
        }
        if (PlanOpener.TryOpen(this, path, allowOther: false, out _) is { } doc) SwitchTo(doc);
    }

    private void SaveNow()
    {
        FlushPendingEdits();
        Save();
    }

    private void SavePlanAs()
    {
        FlushPendingEdits();
        var current = Path.GetDirectoryName(_doc.FilePath);
        var dialog = new SaveFileDialog
        {
            Title = "Finanzplan speichern unter",
            // Verschlüsselte Pläne sind kein JSON – nur als .finanzplan anbieten
            Filter = _doc.IsEncrypted ? "Finanzplan (*.finanzplan)|*.finanzplan" : DataStore.FileFilter,
            FileName = _doc.Name,
            DefaultExt = DataStore.PlanExtension,
            InitialDirectory = !DataStore.IsDefaultPlan(_doc.FilePath) && current is not null
                ? current
                : Directory.CreateDirectory(DataStore.DocumentsFolder).FullName,
        };
        if (dialog.ShowDialog(this) != true) return;

        var target = Path.GetFullPath(dialog.FileName);
        if (_doc.IsEncrypted && !string.Equals(Path.GetExtension(target), DataStore.PlanExtension, StringComparison.OrdinalIgnoreCase))
        {
            target = Path.ChangeExtension(target, DataStore.PlanExtension);
            // Die Überschreiben-Rückfrage des Dialogs galt dem ursprünglichen Namen
            if (File.Exists(target) && !string.Equals(target, _doc.FilePath, StringComparison.OrdinalIgnoreCase)
                && MessageDialog.Show(this, $"„{Path.GetFileName(target)}“ existiert bereits. Ersetzen?", "Speichern unter",
                       MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }
        if (string.Equals(target, _doc.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            Save();
            return;
        }

        var oldPath = _doc.FilePath;
        _doc.FilePath = target;
        if (!Save())
        {
            _doc.FilePath = oldPath;
            return;
        }
        AppSettings.Current.RememberFile(target);
        UpdatePlanUi();
        StatusSaved.Text = $"Gespeichert als {Path.GetFileName(target)} um {DateTime.Now:HH:mm:ss}";
    }

    // ------------------------------------------------------------------ Verschlüsselung

    private async void SetPassword()
    {
        if (_busy) return;
        FlushPendingEdits();
        var dialog = PasswordDialog.SetPassword(this, _doc.Name);
        if (dialog.ShowDialog() != true) return;

        _doc.Key = await CreateKeyAsync(dialog.NewPassword);
        if (!Save())
        {
            _doc.Key = null;
            return;
        }
        UpdatePlanUi();

        var text = $"„{_doc.Name}“ ist jetzt mit AES-256 verschlüsselt. Beim nächsten Öffnen wird das Passwort abgefragt.";
        var plainBackups = DataStore.FindBackups(_doc.FilePath).Where(b => !PlanCrypto.IsEncryptedFile(b)).ToList();
        if (plainBackups.Count == 0)
        {
            MessageDialog.Show(this, text, "Verschlüsselt", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageDialog.Show(this,
            text + $"\n\nIm selben Ordner liegen noch {plainBackups.Count} unverschlüsselte Sicherungskopie(n) dieses Plans " +
            "(angelegt bei früheren Formatumstellungen):\n\n" +
            string.Join("\n", plainBackups.Select(b => "• " + Path.GetFileName(b))) +
            "\n\nSollen diese gelöscht werden, damit die Daten nirgends mehr lesbar herumliegen?",
            "Verschlüsselt", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var failed = new List<string>();
        foreach (var b in plainBackups)
        {
            try { File.Delete(b); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(Path.GetFileName(b)); }
        }
        if (failed.Count > 0)
            MessageDialog.Show(this, "Folgende Dateien konnten nicht gelöscht werden:\n\n" + string.Join("\n", failed),
                "Sicherungskopien", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            StatusSaved.Text = $"{plainBackups.Count} unverschlüsselte Sicherung(en) gelöscht";
    }

    private async void ChangePassword()
    {
        if (_busy || _doc.Key is not { } current) return;
        FlushPendingEdits();
        var dialog = PasswordDialog.ChangePassword(this, _doc.Name, current);
        if (dialog.ShowDialog() != true) return;

        _doc.Key = await CreateKeyAsync(dialog.NewPassword);
        if (!Save())
        {
            _doc.Key = current;
            return;
        }
        UpdatePlanUi();
        MessageDialog.Show(this, $"Das Passwort für „{_doc.Name}“ wurde geändert.", "Passwort geändert",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RemovePassword()
    {
        if (_doc.Key is not { } current) return;
        FlushPendingEdits();
        var dialog = PasswordDialog.RemovePassword(this, _doc.Name, current);
        if (dialog.ShowDialog() != true) return;

        _doc.Key = null;
        if (!Save())
        {
            _doc.Key = current;
            return;
        }
        UpdatePlanUi();
        StatusSaved.Text = $"Verschlüsselung entfernt um {DateTime.Now:HH:mm:ss}";
    }

    // ------------------------------------------------------------------ Datei per Drag & Drop öffnen

    private static string? DroppedPlan(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
        && Path.GetExtension(files[0]).ToLowerInvariant() is DataStore.PlanExtension or ".json"
            ? files[0] : null;

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && DroppedPlan(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (DroppedPlan(e) is not { } path) return;
        e.Handled = true;
        Activate();
        Dispatcher.BeginInvoke(() => OpenPath(path));
    }
}
