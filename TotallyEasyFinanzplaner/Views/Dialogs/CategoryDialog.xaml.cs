using System.Windows;
using System.Windows.Controls;
using TotallyEasyFinanzplaner.Models;
using TotallyEasyFinanzplaner.Services;

namespace TotallyEasyFinanzplaner;

/// <summary>Anlegen, Umbenennen, Zusammenführen und Löschen von Ausgaben-Kategorien.</summary>
public partial class CategoryDialog : Window
{
    private readonly FinanceData _data;
    private readonly Action _save;

    /// <summary>Zeile in der Kategorienliste.</summary>
    public sealed record Row(string Name, bool IsCustom, int Count, string MonthlyText)
    {
        public string CountText => Count == 0 ? "nicht verwendet" : $"{Count} Ausgabe{(Count == 1 ? "" : "n")}";
    }

    /// <param name="save">Speichert die Daten (wird nach jeder Änderung aufgerufen).</param>
    public CategoryDialog(FinanceData data, Action save)
    {
        InitializeComponent();
        _data = data;
        _save = save;
        Refresh();
    }

    private IEnumerable<Entry> Expenses => _data.Entries.Where(e => e.Type == EntryType.Ausgabe);

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private void Refresh(string? select = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(today.Year, today.Month, 1);

        var rows = _data.AllCategories().Select(name =>
        {
            var entries = Expenses.Where(e => Same(e.Category, name)).ToList();
            var monthly = entries.Any(e => !e.IsOneTime) ? Recurring.Describe(entries, month, "−") : "";
            return new Row(name, !Texts.IsDefaultCategory(name), entries.Count, monthly);
        }).ToList();

        CategoryList.ItemsSource = rows;
        CategoryList.SelectedItem = rows.FirstOrDefault(r => Same(r.Name, select));
        CategoryList.ScrollIntoView(CategoryList.SelectedItem ?? rows.FirstOrDefault());
        UpdateButtons();
    }

    private Row? Selected => CategoryList.SelectedItem as Row;

    private void UpdateButtons()
    {
        bool custom = Selected?.IsCustom == true;
        RenameButton.IsEnabled = DeleteButton.IsEnabled = custom;
        RenameButton.ToolTip = DeleteButton.ToolTip = Selected is { IsCustom: false }
            ? "Standardkategorien können nicht geändert werden."
            : null;
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private static string? CheckName(string name) =>
        Same(name, Texts.NoCategory) ? $"\"{Texts.NoCategory}\" ist reserviert." : null;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptDialog("Neue Kategorie", "Name der Kategorie, z. B. \"Haustier\" oder \"Urlaub\":",
            okText: "Anlegen",
            validate: name => CheckName(name)
                ?? (_data.AllCategories().Any(c => Same(c, name)) ? "Diese Kategorie gibt es bereits." : null))
        { Owner = this };
        if (prompt.ShowDialog() != true) return;

        _data.Categories.Add(prompt.Value);
        _save();
        Refresh(prompt.Value);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { IsCustom: true } row) return;

        var prompt = new TextPromptDialog("Kategorie umbenennen", $"Neuer Name für \"{row.Name}\":", row.Name,
            okText: "Umbenennen", validate: CheckName)
        { Owner = this };
        if (prompt.ShowDialog() != true || prompt.Value == row.Name) return;

        var newName = prompt.Value;
        var existing = _data.AllCategories().FirstOrDefault(c => Same(c, newName) && !Same(c, row.Name));
        if (existing is not null)
        {
            var answer = MessageDialog.Show(this,
                $"Die Kategorie \"{existing}\" gibt es bereits.\n\nSollen \"{row.Name}\" und \"{existing}\" zusammengeführt werden? " +
                $"Alle {row.Count} Ausgabe(n) landen dann in \"{existing}\".",
                "Kategorien zusammenführen", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            newName = existing;
        }

        foreach (var entry in Expenses.Where(x => Same(x.Category, row.Name)))
            entry.Category = newName;
        _data.Categories.RemoveAll(c => Same(c, row.Name));
        if (!Texts.IsDefaultCategory(newName) && !_data.Categories.Any(c => Same(c, newName)))
            _data.Categories.Add(newName);

        _save();
        Refresh(newName);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { IsCustom: true } row) return;

        var text = row.Count == 0
            ? $"Kategorie \"{row.Name}\" löschen?"
            : $"Kategorie \"{row.Name}\" löschen?\n\n{row.Count} Ausgabe(n) verwenden diese Kategorie und stehen danach unter \"{Texts.NoCategory}\". " +
              "Die Ausgaben selbst bleiben erhalten.";
        if (MessageDialog.Show(this, text, "Kategorie löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        foreach (var entry in Expenses.Where(x => Same(x.Category, row.Name)))
            entry.Category = null;
        _data.Categories.RemoveAll(c => Same(c, row.Name));

        _save();
        Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
