using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner.Services;

/// <summary>
/// Beispieldaten für den ersten Programmstart (nur wenn noch kein Standardplan existiert). Bewusst allgemein
/// gehalten und relativ zum aktuellen Monat, damit Kalender und Prognose sofort etwas zeigen.
/// </summary>
public static class SeedData
{
    private const string ExampleNote = "Beispiel – anpassen oder löschen";

    public static FinanceData Create()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = new DateOnly(today.Year, today.Month, 1);
        var data = new FinanceData { StartDate = start, StartBalance = 2000m }; // reicht bis zum ersten Gehalt am 28.

        Entry Add(string name, decimal amount, EntryType type, Recurrence rec, DateOnly date, int? day = null,
                  string? category = null, string? job = null, WeekendRule weekend = WeekendRule.Keine)
        {
            var e = new Entry
            {
                Name = name, Amount = amount, Type = type, Recurrence = rec, StartDate = date, DayOfMonth = day,
                Category = category, Job = job, Weekend = weekend, Note = ExampleNote,
            };
            data.Entries.Add(e);
            return e;
        }

        Entry Monthly(string name, decimal amount, int day, string category) =>
            Add(name, amount, EntryType.Ausgabe, Recurrence.Monatlich, start, day, category);

        // Einnahmen
        Add("Gehalt", 2400m, EntryType.Gehalt, Recurrence.Monatlich, start, day: 28, job: "Mein Arbeitgeber",
            weekend: WeekendRule.Vorziehen);
        Add("Steuererstattung", 300m, EntryType.Einnahme, Recurrence.Einmalig, start.AddMonths(2).AddDays(14));

        // Regelmäßige Ausgaben
        Monthly("Miete", 850m, 1, "Wohnen");
        Monthly("Strom", 65m, 15, "Wohnen");
        Monthly("Lebensmittel", 400m, 1, "Lebensmittel & Haushalt");
        Monthly("Handyvertrag", 25m, 5, "Kommunikation");
        Monthly("Internet", 40m, 5, "Kommunikation");
        Monthly("Streaming-Abo", 13m, 10, "Abos & Software");
        Monthly("Sparrate", 150m, 2, "Sparen & Rücklagen");
        Add("Kfz-Versicherung", 240m, EntryType.Ausgabe, Recurrence.Vierteljaehrlich, start, 1, "Versicherungen");
        Add("Haftpflichtversicherung", 60m, EntryType.Ausgabe, Recurrence.Jaehrlich, start.AddMonths(2), 1, "Versicherungen");

        // Einmalige Ausgabe
        Add("Urlaub", 900m, EntryType.Ausgabe, Recurrence.Einmalig, start.AddMonths(4).AddDays(9), category: "Freizeit & Gesundheit");

        return data;
    }
}
