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

    public const int MaxEntries = 100_000;
    public const int MaxSkippedDates = 10_000;

    /// <summary>Größte Plandatei, die geöffnet wird (ein üblicher Plan hat wenige KB).</summary>
    public const long MaxFileBytes = 20 * 1024 * 1024;

    public static bool IsValidDate(DateOnly d) => d >= MinDate && d <= MaxDate;
    public static bool IsValidDate(DateTime d) => IsValidDate(DateOnly.FromDateTime(d));

    public static string DateRangeText => $"{MinDate:dd.MM.yyyy} bis {MaxDate:dd.MM.yyyy}";
}
