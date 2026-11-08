using System.IO;
using System.Windows;
using TotallyEasyFinanzplaner.Services;
using Microsoft.Win32;

namespace TotallyEasyFinanzplaner;

/// <summary>Neuen, leeren Finanzplan anlegen – wahlweise gleich verschlüsselt.</summary>
public partial class NewPlanDialog : Window
{
    private string _folder;

    public NewPlanDialog(string folder)
    {
        InitializeComponent();
        _folder = folder;
        FolderBox.Text = folder;
        NameBox.Text = SuggestName(folder);
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    /// <summary>Vollständiger Pfad der neuen Datei.</summary>
    public string FilePath => Path.Combine(_folder, PlanName + DataStore.PlanExtension);

    /// <summary>Eingegebener Name ohne Endung – wer ".finanzplan" mittippt, bekommt sie nicht doppelt.</summary>
    private string PlanName
    {
        get
        {
            var name = NameBox.Text.Trim();
            return name.EndsWith(DataStore.PlanExtension, StringComparison.OrdinalIgnoreCase)
                ? name[..^DataStore.PlanExtension.Length].TrimEnd()
                : name;
        }
    }

    /// <summary>Passwort oder null, wenn nicht verschlüsselt werden soll.</summary>
    public string? Password => EncryptBox.IsChecked == true ? NewFields.Password : null;

    private static string SuggestName(string folder)
    {
        const string baseName = "Finanzplan";
        for (int i = 1; ; i++)
        {
            var name = i == 1 ? baseName : $"{baseName} {i}";
            if (!File.Exists(Path.Combine(folder, name + DataStore.PlanExtension))) return name;
        }
    }

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        FileText.Text = PlanName.Length > 0 ? "Datei: " + Path.GetFileName(FilePath) : "";

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Speicherort für den Finanzplan wählen", InitialDirectory = _folder };
        if (dialog.ShowDialog(this) != true) return;
        _folder = FolderBox.Text = dialog.FolderName;
        NameBox_TextChanged(sender, null!);
    }

    private void EncryptBox_Changed(object sender, RoutedEventArgs e)
    {
        PasswordPanel.Visibility = EncryptBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (EncryptBox.IsChecked == true) NewFields.FocusFirst();
        else NewFields.Clear();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = PlanName;
        string? error =
            name.Length == 0 ? "Bitte einen Namen eingeben."
            : name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "Der Name darf keines dieser Zeichen enthalten: \\ / : * ? \" < > |"
            : File.Exists(FilePath) ? $"Im Ordner gibt es bereits einen Plan „{name}“. Bitte einen anderen Namen wählen."
            : EncryptBox.IsChecked == true ? NewFields.Validate()
            : null;

        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            if (name.Length == 0 || File.Exists(FilePath)) NameBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
