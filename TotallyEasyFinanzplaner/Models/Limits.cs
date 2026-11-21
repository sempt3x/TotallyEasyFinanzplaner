namespace TotallyEasyFinanzplaner.Models;

/// <summary>
/// Obergrenzen für Plandaten. Sie liegen weit über allem, was im Alltag vorkommt, verhindern aber, dass eine
/// manipulierte Datei das Programm lahmlegt (z. B. Prognose über Jahrtausende) oder beim Rechnen zum
/// Überlauf bringt. Gelten beim Laden einer Datei und bei der Eingabe.
/// </summary>
public static class Limits
{
    public static readonly DateOnly MinDate = new(1900, 1, 1);
    public static readonly DateOnly MaxDate = new(2199, 12, 31);

    /// <summary>Größter Betrag pro Buchung bzw. Startsaldo (1 Billion €).</summary>
    public const decimal MaxAmount = 1_000_000_000_000m;

    /// <summary>
    /// Buchungen pro Plan. Wiederkehrende Buchungen sind eigens begrenzt: Sie bestimmen die Rechenzeit der Prognose
    /// (jede erzeugt einen Termin pro Monat seit dem Stichtag), Einmalzahlungen kosten dagegen kaum etwas.
    /// </summary>
    public const int MaxEntries = 10_000;
    public const int MaxRecurringEntries = 1_000;
    public const int MaxSkippedDates = 10_000;

    /// <summary>Bezeichnung, Arbeitgeber/Job und Kategorie (Zeichen).</summary>
    public const int MaxTextLength = 200;
    public const int MaxNoteLength = 2_000;

    /// <summary>
    /// So weit darf der Stichtag (Startsaldo) zurückliegen. Die Prognose rechnet jeden Tag ab dem Stichtag durch –
    /// ein Stichtag im Jahr 1900 würde jede Aktualisierung um Jahrzehnte verlängern.
    /// </summary>
    public const int MaxStartDateAgeYears = 50;

    public static DateOnly MinStartDate => DateOnly.FromDateTime(DateTime.Today).AddYears(-MaxStartDateAgeYears);
    public static bool IsValidStartDate(DateOnly d) => IsValidDate(d) && d >= MinStartDate;
    public static bool IsValidStartDate(DateTime d) => IsValidStartDate(DateOnly.FromDateTime(d));
    public static string StartDateRangeText => $"{MinStartDate:dd.MM.yyyy} bis {MaxDate:dd.MM.yyyy}";

    /// <summary>Kürzt zu lange Texte (nur Anzeige-Felder – ein Plan soll daran nicht scheitern).</summary>
    public static string? Clip(string? s, int max) => s is { Length: var n } && n > max ? s[..max] : s;

    /// <summary>Größte Plandatei, die geöffnet wird (ein üblicher Plan hat wenige KB).</summary>
    public const long MaxFileBytes = 20 * 1024 * 1024;

    public static bool IsValidDate(DateOnly d) => d >= MinDate && d <= MaxDate;
    public static bool IsValidDate(DateTime d) => IsValidDate(DateOnly.FromDateTime(d));

    public static string DateRangeText => $"{MinDate:dd.MM.yyyy} bis {MaxDate:dd.MM.yyyy}";
}
