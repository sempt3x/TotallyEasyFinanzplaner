using System.Globalization;

namespace TotallyEasyFinanzplaner.Models;

public static class Texts
{
    public static string Of(Recurrence r) => r switch
    {
        Recurrence.Einmalig => "Einmalig",
        Recurrence.Monatlich => "Monatlich",
        Recurrence.Vierteljaehrlich => "Vierteljährlich",
        Recurrence.Halbjaehrlich => "Halbjährlich",
        Recurrence.Jaehrlich => "Jährlich",
        _ => r.ToString(),
    };

    public static string Of(WeekendRule w) => w switch
    {
        WeekendRule.Keine => "Nicht verschieben",
        WeekendRule.Vorziehen => "Auf Freitag vorziehen",
        WeekendRule.Verschieben => "Auf Montag verschieben",
        _ => w.ToString(),
    };

    public static string Of(EntryType t) => t switch
    {
        EntryType.Gehalt => "Gehalt",
        EntryType.Einnahme => "Sonstige Einnahme",
        _ => "Ausgabe",
    };

    public const string NoCategory = "Ohne Kategorie";

    /// <summary>Vorschläge für Ausgaben-Kategorien (eigene Kategorien sind jederzeit möglich).</summary>
    public static IReadOnlyList<string> DefaultCategories { get; } =
    [
        "Wohnen", "Kredite & Finanzierung", "Versicherungen", "Abos & Software", "Kommunikation",
        "Mobilität", "Lebensmittel & Haushalt", "Freizeit & Gesundheit", "Familie", "Shopping",
        "Steuern & Gebühren", "Sparen & Rücklagen", "Sonstiges",
    ];

    public static bool IsDefaultCategory(string name) =>
        DefaultCategories.Contains(name.Trim(), StringComparer.CurrentCultureIgnoreCase);

    public static string Day(int day) => day >= 31 ? "Monatsende" : $"am {day}.";

    /// <summary>Auswahlliste für den Abbuchungstag (1.–30. und Monatsende).</summary>
    public static IReadOnlyList<Option> DayOptions { get; } =
        Enumerable.Range(1, 31).Select(d => new Option(d, Day(d))).ToList();
}

public sealed record Option(object Value, string Text);

public static partial class Money
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Format(decimal v) => v.ToString("C2", De);
    public static string FormatSigned(decimal v) => (v > 0 ? "+" : "") + v.ToString("C2", De);
    public static string Plain(decimal v) => v.ToString("N2", De);

    /// <summary>Akzeptiert "1.234,56", "1234,56", "9.99", "12 €" usw.</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        var s = (text ?? "").Replace("€", "").Replace(" ", "").Replace(" ", "").Trim();
        value = 0;
        if (s.Length == 0) return false;

        int lastDot = s.LastIndexOf('.');
        if (!s.Contains(',') && lastDot >= 0 && s.Count(c => c == '.') == 1 && s.Length - lastDot - 1 is 1 or 2)
            s = s.Replace('.', ','); // "9.99" als Dezimalpunkt interpretieren

        // Tausenderpunkte nur in 3er-Gruppen zulassen: .NET würde "1.2345" sonst still als 12.345 lesen
        var intPart = s.Split(',')[0].TrimStart('+', '-');
        if (intPart.Contains('.') && !ThousandsGrouped().IsMatch(intPart)) return false;

        return decimal.TryParse(s, NumberStyles.Number, De, out value);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial System.Text.RegularExpressions.Regex ThousandsGrouped();
}
