using System.Text.Json.Serialization;

namespace TotallyEasyFinanzplaner.Models;

/// <summary>Gehalt = Lohn aus einem Job (mit <see cref="Entry.Job"/>), Einnahme = sonstige Eingänge (Verkauf, Kredit, Erstattung …).</summary>
public enum EntryType { Ausgabe, Einnahme, Gehalt }

public enum Recurrence { Einmalig, Monatlich, Vierteljaehrlich, Halbjaehrlich, Jaehrlich }

/// <summary>Was passiert, wenn ein Termin auf ein Wochenende fällt.</summary>
public enum WeekendRule { Keine, Vorziehen, Verschieben }

/// <summary>Eine Einnahme oder Ausgabe – einmalig oder wiederkehrend.</summary>
public sealed class Entry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public EntryType Type { get; set; } = EntryType.Ausgabe;
    /// <summary>Immer positiv; das Vorzeichen ergibt sich aus <see cref="Type"/>.</summary>
    public decimal Amount { get; set; }
    public Recurrence Recurrence { get; set; } = Recurrence.Monatlich;
    /// <summary>Einmalig: das Datum. Wiederkehrend: gültig ab (bestimmt auch den Rhythmus-Startmonat).</summary>
    public DateOnly StartDate { get; set; }
    /// <summary>Abbuchungstag im Monat (1–31, 31 = immer Monatsletzter). Null = Tag aus <see cref="StartDate"/>.</summary>
    public int? DayOfMonth { get; set; }
    /// <summary>Letzter möglicher Termin (inklusive). Null = unbegrenzt.</summary>
    public DateOnly? EndDate { get; set; }
    public WeekendRule Weekend { get; set; } = WeekendRule.Keine;
    /// <summary>Einzelne Termine einer Serie, die ausgesetzt werden.</summary>
    public List<DateOnly> SkippedDates { get; set; } = [];
    public string? Note { get; set; }
    /// <summary>Arbeitgeber bzw. Job – nur bei <see cref="EntryType.Gehalt"/>.</summary>
    public string? Job { get; set; }
    /// <summary>Kategorie – nur bei <see cref="EntryType.Ausgabe"/> (z. B. "Versicherungen").</summary>
    public string? Category { get; set; }

    [JsonIgnore] public bool IsIncome => Type != EntryType.Ausgabe;
    [JsonIgnore] public bool IsSalary => Type == EntryType.Gehalt;
    [JsonIgnore] public string JobName => string.IsNullOrWhiteSpace(Job) ? "Ohne Zuordnung" : Job.Trim();
    [JsonIgnore] public string CategoryName => string.IsNullOrWhiteSpace(Category) ? Texts.NoCategory : Category.Trim();
    [JsonIgnore] public string TypeText => Texts.Of(Type);
    [JsonIgnore] public bool IsOneTime => Recurrence == Recurrence.Einmalig;
    [JsonIgnore] public decimal SignedAmount => IsIncome ? Amount : -Amount;

    /// <summary>Effektiver Abbuchungstag (für Anzeige und Bindung in der Liste).</summary>
    [JsonIgnore]
    public int DueDay
    {
        get => DayOfMonth ?? StartDate.Day;
        set => DayOfMonth = Math.Clamp(value, 1, 31);
    }

    [JsonIgnore]
    public int IntervalMonths => Recurrence switch
    {
        Recurrence.Monatlich => 1,
        Recurrence.Vierteljaehrlich => 3,
        Recurrence.Halbjaehrlich => 6,
        Recurrence.Jaehrlich => 12,
        _ => 0,
    };

    // ---- Anzeige ----
    [JsonIgnore] public string AmountText => Money.FormatSigned(SignedAmount);
    [JsonIgnore] public string RecurrenceText => Texts.Of(Recurrence);
    [JsonIgnore] public string DueText => IsOneTime ? StartDate.ToString("dd.MM.yyyy") : Texts.Day(DueDay);
    [JsonIgnore]
    public string PeriodText => IsOneTime
        ? ""
        : $"{StartDate:MM.yyyy} – {(EndDate is { } e ? e.ToString("MM.yyyy") : "")}";

    [JsonIgnore]
    public bool IsFinished
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            // Einmalzahlungen: maßgeblich ist der tatsächliche Buchungstag (ggf. auf Freitag vorgezogen)
            return IsOneTime ? ApplyWeekendRule(StartDate) < today : EndDate is { } e && e < today;
        }
    }

    public bool IsActiveInMonth(DateOnly firstOfMonth)
    {
        var last = firstOfMonth.AddMonths(1).AddDays(-1);
        if (IsOneTime) return StartDate >= firstOfMonth && StartDate <= last;
        return StartDate <= last && (EndDate is not { } e || e >= firstOfMonth);
    }

    public DateOnly ApplyWeekendRule(DateOnly d) => (Weekend, d.DayOfWeek) switch
    {
        (WeekendRule.Vorziehen, DayOfWeek.Saturday) => d.AddDays(-1),
        (WeekendRule.Vorziehen, DayOfWeek.Sunday) => d.AddDays(-2),
        (WeekendRule.Verschieben, DayOfWeek.Saturday) => d.AddDays(2),
        (WeekendRule.Verschieben, DayOfWeek.Sunday) => d.AddDays(1),
        _ => d,
    };

    /// <summary>
    /// Ist der Termin dieser Serie im Monat von <paramref name="nominal"/> ausgesetzt? Eine Serie hat höchstens einen
    /// Termin pro Monat – deshalb zählt nur der Monat. So bleibt eine Aussetzung auch erhalten, wenn später
    /// Abbuchungstag oder Wochenendregel geändert werden.
    /// </summary>
    public bool IsSkipped(DateOnly nominal) =>
        SkippedDates.Any(s => s.Year == nominal.Year && s.Month == nominal.Month);

    /// <summary>Alle tatsächlichen Buchungstage im Zeitraum [from, to].</summary>
    public IEnumerable<DateOnly> OccurrencesBetween(DateOnly from, DateOnly to) =>
        Occurrences(from, to).Select(o => o.Actual);

    /// <summary>
    /// Alle Buchungen im Zeitraum [from, to] (nach tatsächlichem Tag): planmäßiger Termin und – nach der
    /// Wochenendregel – tatsächlicher Buchungstag.
    /// </summary>
    public IEnumerable<(DateOnly Nominal, DateOnly Actual)> Occurrences(DateOnly from, DateOnly to)
    {
        if (IsOneTime)
        {
            var d = ApplyWeekendRule(StartDate);
            if (d >= from && d <= to && !SkippedDates.Contains(d)) yield return (StartDate, d);
            yield break;
        }

        foreach (var o in Schedule(from, to))
            if (!IsSkipped(o.Nominal)) yield return o;
    }

    /// <summary>
    /// Stellt ausgesetzte Termine auf den planmäßigen Termin um. Ältere Dateien speichern den tatsächlichen
    /// (ggf. wegen Wochenende verschobenen) Tag – der kann im Nachbarmonat liegen.
    /// </summary>
    public void NormalizeSkippedDates()
    {
        if (IsOneTime || SkippedDates.Count == 0) return;
        for (int i = 0; i < SkippedDates.Count; i++)
        {
            var s = SkippedDates[i];
            if (s.DayNumber < 3 || s.Year > 9998) continue;
            var near = Schedule(s.AddDays(-3), s.AddDays(3)).ToList();
            if (near.Any(o => o.Nominal == s)) continue; // bereits planmäßiger Termin
            int hit = near.FindIndex(o => o.Actual == s);
            if (hit >= 0) SkippedDates[i] = near[hit].Nominal;
        }
        SkippedDates = [.. SkippedDates.Distinct()];
    }

    /// <summary>Planmäßige und tatsächliche Termine einer Serie (ohne Aussetzungen), tatsächlicher Tag in [from, to].</summary>
    private IEnumerable<(DateOnly Nominal, DateOnly Actual)> Schedule(DateOnly from, DateOnly to)
    {
        if (to < from || IsOneTime) yield break;

        int step = IntervalMonths;
        int startIdx = StartDate.Year * 12 + StartDate.Month - 1;
        int fromIdx = from.Year * 12 + from.Month - 2; // ein Monat Puffer wegen Wochenend-Verschiebung
        int k = fromIdx > startIdx ? (fromIdx - startIdx) / step : 0;

        while (true)
        {
            int idx = startIdx + k++ * step;
            int year = idx / 12, month = idx % 12 + 1;
            if (year > 9998) yield break;

            var nominal = new DateOnly(year, month, Math.Min(DueDay, DateTime.DaysInMonth(year, month)));
            if (EndDate is { } end && nominal > end) yield break;
            if (nominal > to.AddDays(3)) yield break;
            if (nominal < StartDate) continue; // Abbuchungstag liegt im Startmonat vor "gültig ab"

            var actual = ApplyWeekendRule(nominal);
            if (actual < from || actual > to) continue;
            yield return (nominal, actual);
        }
    }
}
