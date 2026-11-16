using System.Text.Json.Serialization;

namespace TotallyEasyFinanzplaner.Models;

/// <summary>Gehalt = Lohn aus einem Job (mit <see cref="Entry.Job"/>), Einnahme = sonstige Eingänge (Verkauf, Kredit, Erstattung …).</summary>
public enum EntryType { Ausgabe, Einnahme, Gehalt }

public enum Recurrence { Einmalig, Monatlich, Vierteljaehrlich, Halbjaehrlich, Jaehrlich }

/// <summary>
/// Was passiert, wenn ein Termin auf ein Wochenende oder einen Feiertag fällt. An solchen Tagen wird nie gebucht:
/// <see cref="Keine"/> (aus älteren Plänen) wirkt deshalb wie <see cref="Verschieben"/> (nächster Werktag).
/// </summary>
public enum WeekendRule { Keine, Vorziehen, Verschieben }

/// <summary>
/// Worüber gekauft bzw. bezahlt wurde (z. B. PayPal in 30 Tagen, Kreditkartenabrechnung). Abgebucht wird am Ende
/// trotzdem vom Konto – die Angabe dient nur der Übersicht. <see cref="Konto"/> = direkt per Überweisung/Lastschrift.
/// </summary>
/// <remarks>Gespeichert wird der Name (nicht die Zahl) – die Reihenfolge hier bestimmt nur die Auswahlliste.</remarks>
public enum PaymentMethod { Konto, Kreditkarte, PayPal, Klarna, Amazon, Ratenkauf, ApplePay, GooglePay }

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
    /// <summary>
    /// Nur vierteljährlich/halbjährlich/jährlich: ein Monat, in dem gebucht wird (1–12, z. B. 2 = Feb/Mai/Aug/Nov bei
    /// vierteljährlich). Null = Rhythmus richtet sich nach dem Monat von <see cref="StartDate"/> (ältere Pläne).
    /// </summary>
    public int? DueMonth { get; set; }
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
    /// <summary>Zahlungsweg. Null (ältere Pläne, nichts gewählt) = direkt über das Konto; wird dann nicht gespeichert.</summary>
    public PaymentMethod? Payment { get; set; }

    [JsonIgnore] public bool IsIncome => Type != EntryType.Ausgabe;
    [JsonIgnore] public bool IsSalary => Type == EntryType.Gehalt;
    [JsonIgnore] public string JobName => string.IsNullOrWhiteSpace(Job) ? "Ohne Zuordnung" : Job.Trim();
    [JsonIgnore] public string CategoryName => string.IsNullOrWhiteSpace(Category) ? Texts.NoCategory : Category.Trim();
    [JsonIgnore] public string TypeText => Texts.Of(Type);
    [JsonIgnore] public PaymentMethod PaymentKind => Payment ?? PaymentMethod.Konto;
    [JsonIgnore] public string PaymentText => Texts.Of(PaymentKind);
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

    /// <summary>Erster Buchungsmonat im Jahr (1 … Intervall); bei monatlich und einmalig 1.</summary>
    [JsonIgnore]
    public int CycleMonth => IntervalMonths <= 1 ? 1 : ((DueMonth ?? StartDate.Month) - 1) % IntervalMonths + 1;

    // ---- Anzeige ----
    [JsonIgnore] public string AmountText => Money.FormatSigned(SignedAmount);
    [JsonIgnore] public string RecurrenceText => Texts.Of(Recurrence);
    /// <summary>Buchungsmonate, z. B. "Feb · Mai · Aug · Nov" – leer bei monatlich und einmalig.</summary>
    [JsonIgnore] public string MonthsText => Texts.Months(Recurrence, CycleMonth);
    [JsonIgnore]
    public string DueText => IsOneTime ? StartDate.ToString("dd.MM.yyyy")
        : MonthsText.Length > 0 ? $"{Texts.Day(DueDay)} ({MonthsText})" : Texts.Day(DueDay);
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

    /// <summary>
    /// Tatsächlicher Buchungstag: Fällt der Termin auf Samstag, Sonntag oder einen Feiertag (<see cref="BusinessDays"/>),
    /// wird auf den vorherigen (<see cref="WeekendRule.Vorziehen"/>) bzw. nächsten Werktag verschoben.
    /// </summary>
    public DateOnly ApplyWeekendRule(DateOnly d) =>
        Weekend == WeekendRule.Vorziehen ? BusinessDays.PreviousOrSame(d) : BusinessDays.NextOrSame(d);

    /// <summary>Höchstens so weit kann ein Termin verschoben werden (z. B. Karfreitag über Ostern + Wochenende).</summary>
    private const int MaxShiftDays = 7;

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
            if (s.DayNumber < MaxShiftDays || s.Year > 9998) continue;
            var near = Schedule(s.AddDays(-MaxShiftDays), s.AddDays(MaxShiftDays)).ToList();
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
        // Auf den ersten Buchungsmonat ab "gültig ab" vorrücken (ohne DueMonth: Monat von StartDate, also Versatz 0)
        startIdx += ((CycleMonth - 1 - startIdx % step) % step + step) % step;
        int fromIdx = from.Year * 12 + from.Month - 2; // ein Monat Puffer wegen Wochenend-/Feiertagsverschiebung
        int k = fromIdx > startIdx ? (fromIdx - startIdx) / step : 0;

        while (true)
        {
            int idx = startIdx + k++ * step;
            int year = idx / 12, month = idx % 12 + 1;
            if (year > 9998) yield break;

            var nominal = new DateOnly(year, month, Math.Min(DueDay, DateTime.DaysInMonth(year, month)));
            if (EndDate is { } end && nominal > end) yield break;
            if (nominal > to.AddDays(MaxShiftDays)) yield break;
            if (nominal < StartDate) continue; // Abbuchungstag liegt im Startmonat vor "gültig ab"

            var actual = ApplyWeekendRule(nominal);
            if (actual < from || actual > to) continue;
            yield return (nominal, actual);
        }
    }
}
