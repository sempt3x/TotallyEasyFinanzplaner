using System.Text.Json.Serialization;

namespace TotallyEasyFinanzplaner.Models;

/// <summary>Inhalt der JSON-Datei.</summary>
public sealed class FinanceData
{
    /// <summary>
    /// Dateiformat. Version 4 (Kreditpläne) wird nur geschrieben, wenn der Plan Kredite enthält: Ältere Programmversionen
    /// lehnen solche Dateien ab, statt die Kredite beim Speichern stillschweigend zu verlieren. Pläne ohne Kredite
    /// bleiben Version 3 und damit weiterhin auch mit älteren Versionen nutzbar.
    /// </summary>
    public const int CurrentVersion = 4;
    /// <summary>Format ohne Kreditpläne. Ältere Dateien werden bis hierhin umgestellt.</summary>
    public const int BaseVersion = 3;
    public int Version { get; set; } = BaseVersion;
    /// <summary>Ab diesem Tag wird der Kontostand berechnet.</summary>
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>Kontostand zu Beginn von <see cref="StartDate"/> (vor den Buchungen dieses Tages).</summary>
    public decimal StartBalance { get; set; }
    public List<Entry> Entries { get; set; } = [];
    /// <summary>Selbst angelegte Ausgaben-Kategorien (zusätzlich zu <see cref="Texts.DefaultCategories"/>).</summary>
    public List<string> Categories { get; set; } = [];
    /// <summary>
    /// Bundesland, dessen Feiertage gelten (an Feiertagen wird nicht gebucht). Null = nur bundesweite Feiertage.
    /// Optional, damit ältere Pläne unverändert geöffnet werden können.
    /// </summary>
    public FederalState? HolidayState { get; set; }

    /// <summary>Kreditpläne und Ratenzahlungen.</summary>
    [JsonIgnore] public List<Loan> Loans { get; set; } = [];

    /// <summary>Gespeichert nur, wenn vorhanden – Pläne ohne Kredite bleiben unverändert.</summary>
    [JsonPropertyName("loans")]
    public List<Loan>? LoansData
    {
        get => Loans.Count == 0 ? null : Loans;
        set => Loans = value ?? [];
    }

    /// <summary>Alle Zahlungen aus den Kreditplänen als Buchungen (für Kalender und Prognose).</summary>
    public IEnumerable<Entry> LoanEntries() => Loans.SelectMany(LoanSchedule.ToEntries);

    /// <summary>Normale Buchungen und Zahlungen aus Kreditplänen.</summary>
    public IEnumerable<Entry> AllBookings() => Entries.Concat(LoanEntries());

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
