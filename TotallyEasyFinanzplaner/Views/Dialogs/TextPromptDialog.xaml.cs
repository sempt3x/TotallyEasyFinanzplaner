using System.Windows;

namespace TotallyEasyFinanzplaner;

/// <summary>Kleiner Dialog zur Eingabe eines Textes (z. B. Kategoriename) mit optionaler Prüfung.</summary>
public partial class TextPromptDialog : Window
{
    private readonly Func<string, string?>? _validate;

    /// <param name="validate">Gibt eine Fehlermeldung zurück oder null, wenn die Eingabe gültig ist.</param>
    public TextPromptDialog(string title, string label, string initial = "", string okText = "OK",
                            Func<string, string?>? validate = null)
    {
        InitializeComponent();
        Title = HeaderText.Text = title;
        LabelText.Text = label;
        OkButton.Content = okText;
        InputBox.Text = initial;
        _validate = validate;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    public string Value => InputBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var error = Value.Length == 0 ? "Bitte einen Namen eingeben." : _validate?.Invoke(Value);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            InputBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
