namespace TotallyEasyFinanzplaner.Models;

/// <summary>Inhalt der JSON-Datei.</summary>
public sealed class FinanceData
{
    public const int CurrentVersion = 3;
    public int Version { get; set; } = CurrentVersion;
    /// <summary>Ab diesem Tag wird der Kontostand berechnet.</summary>
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>Kontostand zu Beginn von <see cref="StartDate"/> (vor den Buchungen dieses Tages).</summary>
    public decimal StartBalance { get; set; }
    public List<Entry> Entries { get; set; } = [];
    /// <summary>Selbst angelegte Ausgaben-Kategorien (zusätzlich zu <see cref="Texts.DefaultCategories"/>).</summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>Alle wählbaren Kategorien: Standard, eigene und in Ausgaben verwendete – ohne Doppelte.</summary>
    public IReadOnlyList<string> AllCategories()
    {
        var custom = Categories
            .Concat(Entries.Where(e => e.Type == EntryType.Ausgabe && !string.IsNullOrWhiteSpace(e.Category)).Select(e => e.Category!.Trim()))
            .Where(c => !Texts.IsDefaultCategory(c))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase);
        return [.. Texts.DefaultCategories, .. custom];
    }
}
