using System.Collections.Concurrent;

namespace TotallyEasyFinanzplaner.Models;

/// <summary>Bundesländer – bestimmen, welche gesetzlichen Feiertage gelten.</summary>
public enum FederalState { BW, BY, BE, BB, HB, HH, HE, MV, NI, NW, RP, SL, SN, ST, SH, TH }

/// <summary>
/// Gesetzliche Feiertage in Deutschland (bundesweit und je Bundesland). Werden lokal berechnet (Ostertermin nach
/// Gauß) – funktioniert also für jedes Jahr und ohne Internet. Nur landesweite Feiertage; Feiertage, die nur in
/// einzelnen Gemeinden gelten (z. B. Mariä Himmelfahrt in Teilen Bayerns, Fronleichnam in Teilen Sachsens), fehlen.
/// </summary>
public static class Holidays
{
    public static string NameOf(FederalState s) => s switch
    {
        FederalState.BW => "Baden-Württemberg",
        FederalState.BY => "Bayern",
        FederalState.BE => "Berlin",
        FederalState.BB => "Brandenburg",
        FederalState.HB => "Bremen",
        FederalState.HH => "Hamburg",
        FederalState.HE => "Hessen",
        FederalState.MV => "Mecklenburg-Vorpommern",
        FederalState.NI => "Niedersachsen",
        FederalState.NW => "Nordrhein-Westfalen",
        FederalState.RP => "Rheinland-Pfalz",
        FederalState.SL => "Saarland",
        FederalState.SN => "Sachsen",
        FederalState.ST => "Sachsen-Anhalt",
        FederalState.SH => "Schleswig-Holstein",
        FederalState.TH => "Thüringen",
        _ => s.ToString(),
    };

    /// <summary>
    /// Feiertage je Jahr und Bundesland. Threadsicher: Pläne mit Passwort werden im Hintergrund geladen,
    /// während das Hauptfenster weiter Feiertage abfragt. Die Jahres-Tabellen werden nach dem Anlegen nur gelesen.
    /// </summary>
    private static readonly ConcurrentDictionary<(int Year, FederalState? State), Dictionary<DateOnly, string>> Cache = new();

    /// <summary>Name des Feiertags an diesem Tag oder null.</summary>
    public static string? Name(DateOnly d, FederalState? state)
    {
        var year = Cache.GetOrAdd((d.Year, state), static key =>
        {
            var table = new Dictionary<DateOnly, string>();
            foreach (var (date, name) in Compute(key.Year, key.State)) table.TryAdd(date, name);
            return table;
        });
        return year.GetValueOrDefault(d);
    }

    /// <summary>Ostersonntag (gregorianisch, Algorithmus nach Gauß/Meeus).</summary>
    public static DateOnly Easter(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    private static IEnumerable<(DateOnly, string)> Compute(int y, FederalState? s)
    {
        var easter = Easter(y);
        bool In(params FederalState[] states) => s is { } st && states.Contains(st);

        // ---- bundesweit
        yield return (new(y, 1, 1), "Neujahr");
        yield return (easter.AddDays(-2), "Karfreitag");
        yield return (easter.AddDays(1), "Ostermontag");
        yield return (new(y, 5, 1), "Tag der Arbeit");
        yield return (easter.AddDays(39), "Christi Himmelfahrt");
        yield return (easter.AddDays(50), "Pfingstmontag");
        if (y >= 1990) yield return (new(y, 10, 3), "Tag der Deutschen Einheit");
        else if (y >= 1954) yield return (new(y, 6, 17), "Tag der deutschen Einheit");
        yield return (new(y, 12, 25), "1. Weihnachtstag");
        yield return (new(y, 12, 26), "2. Weihnachtstag");
        if (y == 2017) yield return (new(y, 10, 31), "Reformationstag (500 Jahre)");
        if (y <= 1994 || In(FederalState.SN)) yield return (RepentanceDay(y), "Buß- und Bettag");

        // ---- je Bundesland
        if (In(FederalState.BW, FederalState.BY, FederalState.ST))
            yield return (new(y, 1, 6), "Heilige Drei Könige");
        if ((y >= 2019 && In(FederalState.BE)) || (y >= 2023 && In(FederalState.MV)))
            yield return (new(y, 3, 8), "Internationaler Frauentag");
        if (y is 2020 or 2025 && In(FederalState.BE))
            yield return (new(y, 5, 8), "Tag der Befreiung");
        if (In(FederalState.BW, FederalState.BY, FederalState.HE, FederalState.NW, FederalState.RP, FederalState.SL))
            yield return (easter.AddDays(60), "Fronleichnam");
        if (In(FederalState.SL))
            yield return (new(y, 8, 15), "Mariä Himmelfahrt");
        if (y >= 2019 && In(FederalState.TH))
            yield return (new(y, 9, 20), "Weltkindertag");
        if ((y >= 1990 && In(FederalState.BB, FederalState.MV, FederalState.SN, FederalState.ST, FederalState.TH))
            || (y >= 2018 && In(FederalState.HB, FederalState.HH, FederalState.NI, FederalState.SH)))
            yield return (new(y, 10, 31), "Reformationstag");
        if (In(FederalState.BW, FederalState.BY, FederalState.NW, FederalState.RP, FederalState.SL))
            yield return (new(y, 11, 1), "Allerheiligen");
    }

    /// <summary>Buß- und Bettag: Mittwoch vor dem 23. November.</summary>
    private static DateOnly RepentanceDay(int y)
    {
        var d = new DateOnly(y, 11, 22);
        while (d.DayOfWeek != DayOfWeek.Wednesday) d = d.AddDays(-1);
        return d;
    }
}

/// <summary>
/// Bankarbeitstage: Samstag, Sonntag und Feiertage des gewählten Bundeslands sind keine Buchungstage.
/// <see cref="State"/> ist das Bundesland des gerade geöffneten Plans (null = nur bundesweite Feiertage).
/// </summary>
public static class BusinessDays
{
    private static volatile StateBox _global = new(null);
    [ThreadStatic] private static StateBox? _override;

    /// <summary>Unveränderlicher Halter, damit das (nullbare) Bundesland atomar gelesen und ersetzt wird.</summary>
    internal sealed record StateBox(FederalState? Value);

    /// <summary>
    /// Bundesland für alle Berechnungen. Setzen gilt programmweit (geöffneter Plan); innerhalb von
    /// <see cref="Use"/> gilt auf diesem Thread vorübergehend ein anderes.
    /// </summary>
    public static FederalState? State
    {
        get => (_override ?? _global).Value;
        set => _global = new StateBox(value);
    }

    /// <summary>
    /// Rechnet auf dem aktuellen Thread vorübergehend mit den Feiertagen von <paramref name="state"/>, ohne das
    /// Bundesland des geöffneten Plans zu verändern (z. B. beim Laden eines anderen Plans im Hintergrund).
    /// <code>using (BusinessDays.Use(plan.HolidayState)) { … }</code>
    /// </summary>
    public static Scope Use(FederalState? state)
    {
        var previous = _override;
        _override = new StateBox(state);
        return new Scope(previous);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly StateBox? _previous;
        internal Scope(StateBox? previous) => _previous = previous;
        public void Dispose() => _override = _previous;
    }

    public static bool IsWeekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static string? HolidayName(DateOnly d) => Holidays.Name(d, State);

    public static bool IsBusinessDay(DateOnly d) => !IsWeekend(d) && HolidayName(d) is null;

    /// <summary>Warum an diesem Tag nicht gebucht wird ("Samstag", "Feiertag: Neujahr") – null an Werktagen.</summary>
    public static string? Reason(DateOnly d) =>
        HolidayName(d) is { } h ? "Feiertag: " + h
        : IsWeekend(d) ? d.ToDateTime(TimeOnly.MinValue).ToString("dddd", Money.De)
        : null;

    /// <summary>Dieser oder der nächste Bankarbeitstag.</summary>
    public static DateOnly NextOrSame(DateOnly d)
    {
        for (int i = 0; i < 14 && !IsBusinessDay(d) && d < DateOnly.MaxValue; i++) d = d.AddDays(1);
        return d;
    }

    /// <summary>Dieser oder der vorherige Bankarbeitstag.</summary>
    public static DateOnly PreviousOrSame(DateOnly d)
    {
        for (int i = 0; i < 14 && !IsBusinessDay(d) && d > DateOnly.MinValue; i++) d = d.AddDays(-1);
        return d;
    }
}
