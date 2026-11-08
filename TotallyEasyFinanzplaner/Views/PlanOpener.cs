using System.IO;
using System.Windows;
using TotallyEasyFinanzplaner.Services;
using Microsoft.Win32;

namespace TotallyEasyFinanzplaner;

public enum OpenOutcome { Opened, Cancelled, OtherPlan, Failed }

/// <summary>Öffnet Finanzpläne inkl. Passwortabfrage und verständlichen Fehlermeldungen.</summary>
public static class PlanOpener
{
    /// <param name="allowOther">Im Passwortdialog "Anderen Plan öffnen…" anbieten (beim Programmstart).</param>
    public static PlanDocument? TryOpen(Window? owner, string path, bool allowOther, out OpenOutcome outcome)
    {
        path = Path.GetFullPath(path);
        var name = DataStore.DisplayName(path);
        bool isDefault = DataStore.IsDefaultPlan(path);
        try
        {
            if (!File.Exists(path) && !isDefault)
            {
                AppSettings.Current.ForgetFile(path);
                throw new FileNotFoundException($"Die Datei wurde nicht gefunden:\n{path}");
            }

            if (!PlanCrypto.IsEncryptedFile(path))
            {
                outcome = OpenOutcome.Opened;
                return DataStore.Open(path, recoverIfDamaged: isDefault);
            }

            PlanDocument? doc = null;
            var dialog = PasswordDialog.Unlock(owner, name, path, password =>
            {
                try
                {
                    doc = DataStore.Open(path, password, recoverIfDamaged: isDefault);
                    return null;
                }
                catch (WrongPasswordException) { return "Das Passwort ist falsch."; }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    return "Die Datei konnte nicht gelesen werden: " + ex.Message;
                }
            }, allowOther);

            bool ok = dialog.ShowDialog() == true && doc is not null;
            outcome = ok ? OpenOutcome.Opened : dialog.WantsOtherPlan ? OpenOutcome.OtherPlan : OpenOutcome.Cancelled;
            return ok ? doc : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageDialog.Show(owner, $"„{name}“ konnte nicht geöffnet werden.\n\n{ex.Message}", "Öffnen fehlgeschlagen",
                MessageBoxButton.OK, MessageBoxImage.Error);
            outcome = OpenOutcome.Failed;
            return null;
        }
    }

    /// <summary>Dateiauswahl "Finanzplan öffnen". Gibt den gewählten Pfad zurück oder null.</summary>
    public static string? Browse(Window? owner, string? currentPath)
    {
        var folder = currentPath is not null && !DataStore.IsDefaultPlan(currentPath)
            ? Path.GetDirectoryName(currentPath)
            : Directory.Exists(DataStore.DocumentsFolder) ? DataStore.DocumentsFolder : null;
        var dialog = new OpenFileDialog
        {
            Title = "Finanzplan öffnen",
            Filter = DataStore.OpenFilter,
            InitialDirectory = folder ?? "",
        };
        bool ok = owner is { IsVisible: true } ? dialog.ShowDialog(owner) == true : dialog.ShowDialog() == true;
        return ok ? dialog.FileName : null;
    }

    /// <summary>
    /// Beim Start: zuletzt geöffneten Plan (oder den aus der Befehlszeile) öffnen, sonst den Standardplan.
    /// Gibt null zurück, wenn der Benutzer abbricht – dann wird das Programm beendet.
    /// </summary>
    public static PlanDocument? OpenAtStartup(string? argumentPath)
    {
        var last = AppSettings.Current.LastFile;
        string path = argumentPath ?? (last is not null && File.Exists(last) ? last : DataStore.DefaultPlanPath);

        while (true)
        {
            var doc = TryOpen(null, path, allowOther: true, out var outcome);
            switch (outcome)
            {
                case OpenOutcome.Opened:
                    return doc;
                case OpenOutcome.OtherPlan:
                    path = Browse(null, path) ?? path;
                    break;
                case OpenOutcome.Failed when !DataStore.IsDefaultPlan(path):
                    path = DataStore.DefaultPlanPath; // Ausweichen auf den Standardplan
                    break;
                default:
                    return null;
            }
        }
    }
}
