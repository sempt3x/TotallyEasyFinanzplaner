using System.Windows;
using System.Windows.Controls;
using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner;

/// <summary>Dialog zum Anlegen/Bearbeiten. Schreibt erst bei "Speichern" in das übergebene Objekt.</summary>
public partial class EntryDialog : Window
{
    private readonly Entry _target;
    private readonly List<DateOnly> _skipped;
    private bool _ready;

    public EntryDialog(Entry target, bool isNew, IEnumerable<string>? knownJobs = null, IEnumerable<string>? knownCategories = null)
    {
        InitializeComponent();
        _target = target;
        _skipped = [.. target.SkippedDates];

        HeaderText.Text = !isNew ? "Eintrag bearbeiten"
            : target.IsOneTime ? $"Neue Einmalzahlung am {target.StartDate:dd.MM.yyyy}"
            : target.Type == EntryType.Gehalt ? "Neues Gehalt"
            : target.Type == EntryType.Einnahme ? "Neue Einnahme" : "Neue Ausgabe";
        Title = HeaderText.Text;

        TypeBox.ItemsSource = Enum.GetValues<EntryType>().Select(t => new Option(t, Texts.Of(t))).ToList();
        RecurrenceBox.ItemsSource = Enum.GetValues<Recurrence>().Select(r => new Option(r, Texts.Of(r))).ToList();
        WeekendBox.ItemsSource = Enum.GetValues<WeekendRule>().Select(w => new Option(w, Texts.Of(w))).ToList();

        NameBox.Text = target.Name;
        TypeBox.SelectedValue = target.Type;
        JobBox.ItemsSource = (knownJobs ?? []).Where(j => !string.IsNullOrWhiteSpace(j)).Distinct().Order().ToList();
        JobBox.Text = target.Job ?? "";
        CategoryBox.ItemsSource = Texts.DefaultCategories
            .Concat((knownCategories ?? []).Where(k => !string.IsNullOrWhiteSpace(k) && !Texts.DefaultCategories.Contains(k)).Distinct().Order())
            .ToList();
        CategoryBox.Text = target.Category ?? "";
        AmountBox.Text = isNew && target.Amount == 0 ? "" : Money.Plain(target.Amount);
        RecurrenceBox.SelectedValue = target.Recurrence;
        DayBox.ItemsSource = Texts.DayOptions;
        DayBox.SelectedValue = target.DueDay;
        StartPicker.SelectedDate = target.StartDate.ToDateTime(TimeOnly.MinValue);
        HasEndBox.IsChecked = target.EndDate.HasValue;
        EndPicker.SelectedDate = target.EndDate?.ToDateTime(TimeOnly.MinValue);
        WeekendBox.SelectedValue = target.Weekend;
        NoteBox.Text = target.Note ?? "";

        _ready = true;
        UpdateUi();
        Loaded += (_, _) => (string.IsNullOrEmpty(NameBox.Text) ? NameBox : AmountBox).Focus();
    }

    private EntryType SelectedType => TypeBox.SelectedValue is EntryType t ? t : EntryType.Ausgabe;

    private Recurrence SelectedRecurrence => RecurrenceBox.SelectedValue is Recurrence r ? r : Recurrence.Monatlich;

    private void UpdateUi()
    {
        if (!_ready) return;
        var rec = SelectedRecurrence;
        bool once = rec == Recurrence.Einmalig;

        DateLabel.Text = once ? "Datum" : "Gültig ab";
        EndLabel.Visibility = EndPanel.Visibility = once ? Visibility.Collapsed : Visibility.Visible;
        DayLabel.Visibility = DayBox.Visibility = once ? Visibility.Collapsed : Visibility.Visible;
        EndPicker.IsEnabled = HasEndBox.IsChecked == true;
        JobLabel.Visibility = JobBox.Visibility = SelectedType == EntryType.Gehalt ? Visibility.Visible : Visibility.Collapsed;
        CategoryLabel.Visibility = CategoryBox.Visibility = SelectedType == EntryType.Ausgabe ? Visibility.Visible : Visibility.Collapsed;
        DayLabel.Text = SelectedType == EntryType.Ausgabe ? "Abbuchungstag" : "Eingangstag";

        var dayText = DayBox.SelectedValue is int day ? Texts.Day(day) : "?";
        var from = StartPicker.SelectedDate is { } s ? s.ToString("dd.MM.yyyy") : "?";
        HintText.Text = once
            ? "Die Zahlung wird genau an diesem Datum gebucht."
            : $"Wird {Texts.Of(rec).ToLower()} {dayText} {(SelectedType == EntryType.Ausgabe ? "abgebucht" : "gutgeschrieben")}, erstmals ab dem {from}" +
              (rec == Recurrence.Monatlich ? "." : " (der Monat des Datums legt den Rhythmus fest).") +
              " Hat ein Monat weniger Tage, wird der letzte Tag des Monats genommen.";

        SkippedPanel.Visibility = !once && _skipped.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Ausgesetzt wird jeweils der Termin eines Monats – unabhängig vom Abbuchungstag
        SkippedText.Text = "Ausgesetzte Termine: " + string.Join(", ", _skipped
            .Select(d => new DateOnly(d.Year, d.Month, 1)).Distinct().Order()
            .Select(d => d.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Money.De)));
    }

    private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();
    private void RecurrenceBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();
    private void StartPicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e) => UpdateUi();
    private void DayBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();
    private void HasEndBox_Changed(object sender, RoutedEventArgs e) => UpdateUi();

    private void RestoreSkipped_Click(object sender, RoutedEventArgs e)
    {
        _skipped.Clear();
        UpdateUi();
    }

    /// <summary>
    /// Prüft, ob zwischen "gültig ab" und Enddatum mindestens ein Termin liegt. Der erste Termin kann später als
    /// "gültig ab" sein (z. B. gültig ab 20.03., Abbuchung am 5. → erster Termin 05.04.).
    /// </summary>
    private string? CheckHasOccurrence(DateOnly start, DateOnly end, Recurrence rec)
    {
        var probe = new Entry
        {
            Recurrence = rec,
            StartDate = start,
            DayOfMonth = DayBox.SelectedValue is int d ? d : start.Day,
            Weekend = WeekendBox.SelectedValue is WeekendRule w ? w : WeekendRule.Keine,
        };
        // Wochenendregel kann den Buchungstag um bis zu 2 Tage verschieben
        var window = (From: start.AddDays(-3), To: end.AddDays(3));
        probe.EndDate = end;
        if (probe.OccurrencesBetween(window.From, window.To).Any()) return null;

        probe.EndDate = null;
        var first = probe.OccurrencesBetween(window.From, start.AddYears(2)).FirstOrDefault();
        return $"Bis zum Enddatum ({end:dd.MM.yyyy}) gibt es keinen einzigen Termin." +
               (first != default ? $" Der erste Termin wäre am {first:dd.MM.yyyy}." : "") +
               " Bitte Enddatum, Abbuchungstag oder \"gültig ab\" anpassen.";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? error = null;
        var name = NameBox.Text.Trim();
        var rec = SelectedRecurrence;
        DateOnly? end = null;

        if (name.Length == 0) error = "Bitte eine Bezeichnung eingeben.";
        else if (!Money.TryParse(AmountBox.Text, out var amount) || amount <= 0)
            error = "Bitte einen positiven Betrag eingeben (z. B. 49,99). Ob Einnahme oder Ausgabe, wird über \"Art\" gewählt.";
        else if (SelectedType == EntryType.Gehalt && string.IsNullOrWhiteSpace(JobBox.Text))
            error = "Bitte den Arbeitgeber bzw. Job angeben, zu dem dieses Gehalt gehört.";
        else if (StartPicker.SelectedDate is not { } start) error = "Bitte ein Datum wählen.";
        else
        {
            if (rec != Recurrence.Einmalig && HasEndBox.IsChecked == true)
            {
                if (EndPicker.SelectedDate is not { } endDt) error = "Bitte ein Enddatum wählen oder den Haken entfernen.";
                else if (endDt.Date < start.Date) error = "Das Enddatum liegt vor dem Datum \"gültig ab\".";
                else
                {
                    end = DateOnly.FromDateTime(endDt);
                    error = CheckHasOccurrence(DateOnly.FromDateTime(start), end.Value, rec);
                }
            }

            if (error is null)
            {
                _target.Name = name;
                _target.Type = SelectedType;
                _target.Job = SelectedType == EntryType.Gehalt ? JobBox.Text.Trim() : null;
                _target.Category = SelectedType == EntryType.Ausgabe && !string.IsNullOrWhiteSpace(CategoryBox.Text)
                                   && CategoryBox.Text.Trim() != Texts.NoCategory
                    ? CategoryBox.Text.Trim() : null;
                _target.Amount = amount;
                _target.Recurrence = rec;
                _target.StartDate = DateOnly.FromDateTime(start);
                _target.DayOfMonth = rec == Recurrence.Einmalig ? null
                    : DayBox.SelectedValue is int d ? d : _target.StartDate.Day;
                _target.EndDate = rec == Recurrence.Einmalig ? null : end;
                _target.Weekend = WeekendBox.SelectedValue is WeekendRule w ? w : WeekendRule.Keine;
                _target.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
                _target.SkippedDates = rec == Recurrence.Einmalig ? [] : [.. _skipped];
                DialogResult = true;
                return;
            }
        }

        MessageDialog.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
