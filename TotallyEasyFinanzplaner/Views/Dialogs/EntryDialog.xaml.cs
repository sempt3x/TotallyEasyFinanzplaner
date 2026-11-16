using System.Windows;
using System.Windows.Controls;
using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner;

/// <summary>Dialog zum Anlegen/Bearbeiten. Schreibt erst bei "Speichern" in das übergebene Objekt.</summary>
public partial class EntryDialog : Window
{
    private const int PreviewCount = 4;

    private readonly Entry _target;
    private readonly List<DateOnly> _skipped;
    /// <summary>Gewählter Buchungsmonat (bleibt beim Wechsel des Rhythmus erhalten, z. B. Feb → Feb · Mai · Aug · Nov).</summary>
    private int _cycleMonth;
    private bool _ready;
    private bool _rebuildingMonths;

    /// <summary>
    /// Regelmäßige Einträge bleiben regelmäßig, Einmalzahlungen bleiben einmalig – passend zu den getrennten Tabellen.
    /// Mit <paramref name="lockType"/> ist zusätzlich die Art fest (z. B. "+ Ausgabe" legt nur Ausgaben an).
    /// </summary>
    public EntryDialog(Entry target, bool isNew, IEnumerable<string>? knownJobs = null, IEnumerable<string>? knownCategories = null,
                       bool lockType = false)
    {
        InitializeComponent();
        _target = target;
        _skipped = [.. target.SkippedDates];
        _cycleMonth = target.DueMonth ?? target.StartDate.Month;
        bool oneTime = target.IsOneTime;

        var kind = target.Type switch
        {
            EntryType.Gehalt => "Gehalt",
            EntryType.Einnahme => oneTime ? "einmalige Einnahme" : "regelmäßige Einnahme",
            _ => oneTime ? "einmalige Ausgabe" : "regelmäßige Ausgabe",
        };
        HeaderText.Text = !isNew ? (oneTime ? "Einmalzahlung bearbeiten" : "Regelmäßige Buchung bearbeiten")
            : !lockType ? $"Neue Einmalzahlung am {target.StartDate:dd.MM.yyyy}"
            : target.Type == EntryType.Gehalt ? "Neues Gehalt"
            : $"Neue {kind}";
        Title = HeaderText.Text;

        TypeBox.ItemsSource = Enum.GetValues<EntryType>().Select(t => new Option(t, Texts.Of(t))).ToList();
        // Einmalig ↔ regelmäßig ist nicht umschaltbar; bei Einmalzahlungen entfällt die Rhythmus-Auswahl ganz
        RecurrenceBox.ItemsSource = Enum.GetValues<Recurrence>()
            .Where(r => oneTime == (r == Recurrence.Einmalig))
            .Select(r => new Option(r, Texts.Of(r))).ToList();
        RecurrenceLabel.Visibility = RecurrenceBox.Visibility = oneTime ? Visibility.Collapsed : Visibility.Visible;
        TypeLabel.Visibility = TypeBox.Visibility = lockType ? Visibility.Collapsed : Visibility.Visible;
        // An Wochenenden/Feiertagen wird nie gebucht – nur noch die Richtung ist wählbar ("Keine" aus alten Plänen = nächster Werktag)
        WeekendBox.ItemsSource = new[] { WeekendRule.Verschieben, WeekendRule.Vorziehen }.Select(w => new Option(w, Texts.Of(w))).ToList();

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
        WeekendBox.SelectedValue = target.Weekend == WeekendRule.Vorziehen ? WeekendRule.Vorziehen : WeekendRule.Verschieben;
        NoteBox.Text = target.Note ?? "";

        _ready = true;
        RebuildMonthOptions();
        UpdateUi();
        Loaded += (_, _) => (string.IsNullOrEmpty(NameBox.Text) ? NameBox : AmountBox).Focus();
    }

    private EntryType SelectedType => TypeBox.SelectedValue is EntryType t ? t : EntryType.Ausgabe;

    private Recurrence SelectedRecurrence => RecurrenceBox.SelectedValue is Recurrence r ? r : Recurrence.Monatlich;

    private WeekendRule SelectedWeekend => WeekendBox.SelectedValue is WeekendRule w ? w : WeekendRule.Verschieben;

    private static int IntervalOf(Recurrence r) => new Entry { Recurrence = r }.IntervalMonths;

    /// <summary>Gewählter Buchungsmonat – nur bei vierteljährlich/halbjährlich/jährlich, sonst null.</summary>
    private int? SelectedDueMonth => IntervalOf(SelectedRecurrence) > 1 && MonthBox.SelectedValue is int m ? m : null;

    /// <summary>Auswahl der Buchungsmonate passend zum Rhythmus (vierteljährlich: 3 Möglichkeiten, jährlich: 12).</summary>
    private void RebuildMonthOptions()
    {
        int step = IntervalOf(SelectedRecurrence);
        _rebuildingMonths = true;
        if (step > 1)
        {
            MonthBox.ItemsSource = Texts.MonthOptions(SelectedRecurrence);
            MonthBox.SelectedValue = (_cycleMonth - 1) % step + 1;
        }
        else MonthBox.ItemsSource = null;
        _rebuildingMonths = false;
    }

    /// <summary>Eintrag mit den aktuellen Eingaben – für Vorschau und Prüfung, ohne das Original zu ändern.</summary>
    private Entry? BuildProbe(DateOnly? end = null)
    {
        if (StartPicker.SelectedDate is not { } s || !Limits.IsValidDate(s)) return null;
        var start = DateOnly.FromDateTime(s);
        return new Entry
        {
            Recurrence = SelectedRecurrence,
            StartDate = start,
            DayOfMonth = DayBox.SelectedValue is int d ? d : start.Day,
            DueMonth = SelectedDueMonth,
            Weekend = SelectedWeekend,
            EndDate = end,
        };
    }

    private static string Long(DateOnly d) => d.ToDateTime(TimeOnly.MinValue).ToString("dddd, dd.MM.yyyy", Money.De);

    private void UpdateUi()
    {
        if (!_ready) return;
        var rec = SelectedRecurrence;
        bool once = rec == Recurrence.Einmalig;
        bool byMonth = IntervalOf(rec) > 1;

        DateLabel.Text = once ? "Datum" : "Gültig ab";
        EndLabel.Visibility = EndPanel.Visibility = once ? Visibility.Collapsed : Visibility.Visible;
        DayLabel.Visibility = DayBox.Visibility = once ? Visibility.Collapsed : Visibility.Visible;
        MonthLabel.Visibility = MonthBox.Visibility = byMonth ? Visibility.Visible : Visibility.Collapsed;
        EndPicker.IsEnabled = HasEndBox.IsChecked == true;
        JobLabel.Visibility = JobBox.Visibility = SelectedType == EntryType.Gehalt ? Visibility.Visible : Visibility.Collapsed;
        CategoryLabel.Visibility = CategoryBox.Visibility = SelectedType == EntryType.Ausgabe ? Visibility.Visible : Visibility.Collapsed;
        DayLabel.Text = SelectedType == EntryType.Ausgabe ? "Abbuchungstag" : "Eingangstag";
        MonthLabel.Text = SelectedType == EntryType.Ausgabe ? "Abbuchungsmonate" : "Eingangsmonate";

        var probe = BuildProbe();
        HintText.Text = once ? OneTimeHint(probe) : RecurringHint(probe, rec);

        SkippedPanel.Visibility = !once && _skipped.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Ausgesetzt wird jeweils der Termin eines Monats – unabhängig vom Abbuchungstag
        SkippedText.Text = "Ausgesetzte Termine: " + string.Join(", ", _skipped
            .Select(d => new DateOnly(d.Year, d.Month, 1)).Distinct().Order()
            .Select(d => d.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Money.De)));
    }

    private static string OneTimeHint(Entry? probe)
    {
        if (probe is null) return "Bitte ein Datum wählen.";
        var date = probe.StartDate;
        if (BusinessDays.IsWeekend(date))
            return $"{Long(date)}: An Wochenenden wird nicht gebucht – bitte einen Werktag wählen " +
                   $"(nächster Werktag: {Long(BusinessDays.NextOrSame(date))}).";
        if (BusinessDays.HolidayName(date) is { } holiday)
            return $"{holiday} ist ein Feiertag – die Buchung erfolgt am {Long(probe.ApplyWeekendRule(date))}.";
        return "Die Zahlung wird genau an diesem Datum gebucht.";
    }

    private string RecurringHint(Entry? probe, Recurrence rec)
    {
        var dayText = DayBox.SelectedValue is int day ? Texts.Day(day) : "?";
        var months = SelectedDueMonth is { } m ? $" ({Texts.Months(rec, m)})" : "";
        var from = probe is null ? "?" : probe.StartDate.ToString("dd.MM.yyyy");
        var text = $"Wird {Texts.Of(rec).ToLower()} {dayText}{months} " +
                   $"{(SelectedType == EntryType.Ausgabe ? "abgebucht" : "gutgeschrieben")}, frühestens ab dem {from}. " +
                   "Hat ein Monat weniger Tage, wird der letzte Tag des Monats genommen. " +
                   "Fällt ein Termin auf Wochenende oder Feiertag, wird am " +
                   (SelectedWeekend == WeekendRule.Vorziehen ? "vorherigen" : "nächsten") + " Werktag gebucht.";
        if (probe is null) return text;

        // Vorschau der nächsten Termine (ab heute bzw. ab "gültig ab")
        if (HasEndBox.IsChecked == true && EndPicker.SelectedDate is { } endDt && Limits.IsValidDate(endDt))
            probe.EndDate = DateOnly.FromDateTime(endDt);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = probe.StartDate > today ? probe.StartDate : today;
        var next = probe.Occurrences(start, start.AddYears(3)).Take(PreviewCount)
            .Select(o => o.Actual.ToString("dd.MM.yyyy") +
                         (o.Nominal != o.Actual
                             ? $" (statt {o.Nominal:dd.MM.}, {BusinessDays.Reason(o.Nominal)})"
                             : ""))
            .ToList();
        return next.Count == 0 ? text : text + "\n\nNächste Termine: " + string.Join(" · ", next);
    }

    private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();

    private void RecurrenceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        RebuildMonthOptions();
        UpdateUi();
    }

    private void MonthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _rebuildingMonths) return;
        if (MonthBox.SelectedValue is int m) _cycleMonth = m;
        UpdateUi();
    }

    private void StartPicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e) => UpdateUi();
    private void DayBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();
    private void WeekendBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();
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
    private string? CheckHasOccurrence(DateOnly start, DateOnly end)
    {
        var probe = BuildProbe(end)!;
        // Wochenende/Feiertage können den Buchungstag um bis zu einer Woche verschieben
        var window = (From: start.AddDays(-7), To: end.AddDays(7));
        if (probe.OccurrencesBetween(window.From, window.To).Any()) return null;

        probe.EndDate = null;
        var first = probe.OccurrencesBetween(window.From, start.AddYears(2)).FirstOrDefault();
        return $"Bis zum Enddatum ({end:dd.MM.yyyy}) gibt es keinen einzigen Termin." +
               (first != default ? $" Der erste Termin wäre am {first:dd.MM.yyyy}." : "") +
               " Bitte Enddatum, Abbuchungsmonat/-tag oder \"gültig ab\" anpassen.";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? error = null;
        var name = NameBox.Text.Trim();
        var rec = SelectedRecurrence;
        DateOnly? end = null;

        if (name.Length == 0) error = "Bitte eine Bezeichnung eingeben.";
        else if (!Money.TryParse(AmountBox.Text, out var amount) || amount <= 0)
            error = "Bitte einen positiven Betrag eingeben (z. B. 49,99), höchstens " + Money.Plain(Limits.MaxAmount) + ". " +
                    (TypeBox.IsVisible ? "Ob Einnahme oder Ausgabe, wird über \"Art\" gewählt." : "");
        else if (SelectedType == EntryType.Gehalt && string.IsNullOrWhiteSpace(JobBox.Text))
            error = "Bitte den Arbeitgeber bzw. Job angeben, zu dem dieses Gehalt gehört.";
        else if (StartPicker.SelectedDate is not { } start) error = "Bitte ein Datum wählen.";
        else if (!Limits.IsValidDate(start)) error = $"Das Datum muss zwischen {Limits.DateRangeText} liegen.";
        else if (rec == Recurrence.Einmalig && BusinessDays.IsWeekend(DateOnly.FromDateTime(start)))
            error = $"{Long(DateOnly.FromDateTime(start))}: An Samstagen und Sonntagen wird nicht gebucht.\n\n" +
                    $"Bitte einen Werktag wählen, z. B. {Long(BusinessDays.NextOrSame(DateOnly.FromDateTime(start)))}.";
        else if (IntervalOf(rec) > 1 && SelectedDueMonth is null) error = "Bitte die Buchungsmonate wählen.";
        else
        {
            if (rec != Recurrence.Einmalig && HasEndBox.IsChecked == true)
            {
                if (EndPicker.SelectedDate is not { } endDt) error = "Bitte ein Enddatum wählen oder den Haken entfernen.";
                else if (!Limits.IsValidDate(endDt)) error = $"Das Enddatum muss zwischen {Limits.DateRangeText} liegen.";
                else if (endDt.Date < start.Date) error = "Das Enddatum liegt vor dem Datum \"gültig ab\".";
                else
                {
                    end = DateOnly.FromDateTime(endDt);
                    error = CheckHasOccurrence(DateOnly.FromDateTime(start), end.Value);
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
                _target.DueMonth = SelectedDueMonth;
                _target.EndDate = rec == Recurrence.Einmalig ? null : end;
                _target.Weekend = SelectedWeekend;
                _target.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
                _target.SkippedDates = rec == Recurrence.Einmalig ? [] : [.. _skipped];
                DialogResult = true;
                return;
            }
        }

        MessageDialog.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
