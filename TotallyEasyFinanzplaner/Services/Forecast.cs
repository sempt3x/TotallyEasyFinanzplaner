using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner.Services;

/// <summary>Eine Buchung an einem konkreten Tag.</summary>
/// <param name="Nominal">Planmäßiger Termin (vor Anwendung der Wochenendregel) – wird beim Aussetzen gespeichert.</param>
public sealed record DayItem(Entry Entry, DateOnly Date, DateOnly Nominal)
{
    public decimal Signed => Entry.SignedAmount;
    public string Name => Entry.Name;
    public bool IsIncome => Entry.IsIncome;
    public bool IsOneTime => Entry.IsOneTime;
    public string AmountText => Money.FormatSigned(Signed);
    public string ShortAmount => (Signed > 0 ? "+" : "−") + Money.Plain(Math.Abs(Signed));
    public bool IsSalary => Entry.IsSalary;
    public PaymentMethod Payment => Entry.PaymentKind;
    /// <summary>Wegen Wochenende/Feiertag auf einen anderen Tag verschoben.</summary>
    public bool IsShifted => Nominal != Date;
    /// <summary>z. B. "Planmäßig 25.12. (Feiertag: 1. Weihnachtstag)" – leer, wenn nicht verschoben.</summary>
    public string ShiftText => IsShifted
        ? $"Planmäßig {Nominal:dd.MM.}" + (BusinessDays.Reason(Nominal) is { } r ? $" ({r})" : "")
        : "";
    public string SubText => (IsShifted ? ShiftText + " · " : "")
                             + (IsSalary ? $"Gehalt · {Entry.JobName} · " : "")
                             + (Entry.Type == EntryType.Ausgabe ? $"{Entry.CategoryName} · " : "")
                             + (IsOneTime ? "Einmalzahlung" : Entry.RecurrenceText)
                             + (string.IsNullOrWhiteSpace(Entry.Note) ? "" : " · " + Entry.Note);
}

public sealed class DayInfo
{
    public required DateOnly Date { get; init; }
    public List<DayItem> Items { get; } = [];
    /// <summary>Buchungen, die planmäßig an diesem Tag wären, wegen Wochenende/Feiertag aber an einem anderen Tag erfolgen.</summary>
    public List<DayItem> Deferred { get; } = [];
    /// <summary>Feiertag an diesem Tag (gemäß Bundesland des Plans) oder null.</summary>
    public string? Holiday => BusinessDays.HolidayName(Date);
    public bool IsBusinessDay => BusinessDays.IsBusinessDay(Date);
    /// <summary>Kontostand am Tagesende; null vor dem Startdatum.</summary>
    public decimal? Balance { get; set; }
    public decimal Net => Items.Sum(i => i.Signed);
    public bool HasOneTime => Items.Any(i => i.IsOneTime);
}

public static class Forecast
{
    public static SortedDictionary<DateOnly, DayInfo> Compute(FinanceData data, DateOnly from, DateOnly to)
    {
        var begin = from < data.StartDate ? from : data.StartDate;
        var days = new SortedDictionary<DateOnly, DayInfo>();
        for (var d = begin; d <= to; d = d.AddDays(1))
            days[d] = new DayInfo { Date = d };

        foreach (var entry in data.Entries)
            foreach (var (nominal, actual) in entry.Occurrences(begin, to))
            {
                var item = new DayItem(entry, actual, nominal);
                days[actual].Items.Add(item);
                if (nominal != actual && days.TryGetValue(nominal, out var planned)) planned.Deferred.Add(item);
            }

        decimal balance = data.StartBalance;
        foreach (var day in days.Values)
        {
            day.Items.Sort((a, b) => a.IsIncome != b.IsIncome
                ? (a.IsIncome ? -1 : 1)
                : Math.Abs(b.Signed).CompareTo(Math.Abs(a.Signed)));

            if (day.Date >= data.StartDate)
            {
                balance += day.Net;
                day.Balance = balance;
            }
        }
        return days;
    }
}

public sealed class MonthStats
{
    public DateOnly Month { get; init; }
    public decimal? Opening { get; init; }
    public decimal? Closing { get; init; }
    public decimal? Min { get; init; }
    public DateOnly? MinDate { get; init; }
    public decimal Income { get; init; }
    public decimal Expense { get; init; }
    /// <summary>Anteil der Einnahmen aus Gehältern.</summary>
    public decimal Salary { get; init; }
    public decimal OtherIncome => Income - Salary;

    public string MonthText => Month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Money.De);
    public string IncomeText => "+" + Money.Plain(Income);
    public string SalaryText => "+" + Money.Plain(Salary);
    public string OtherIncomeText => "+" + Money.Plain(OtherIncome);
    public string ExpenseText => "−" + Money.Plain(Expense);
    public string ClosingText => Closing is { } c ? Money.Plain(c) : "–";
    public string MinText => Min is { } m ? $"{Money.Plain(m)} ({MinDate:dd.MM.})" : "–";
    public bool ClosingNegative => Closing < 0;
    public bool MinNegative => Min < 0;

    public static MonthStats Create(FinanceData data, IReadOnlyDictionary<DateOnly, DayInfo> days, DateOnly month)
    {
        var last = month.AddMonths(1).AddDays(-1);
        decimal? opening = null;
        if (days.TryGetValue(month.AddDays(-1), out var prev) && prev.Balance is { } pb) opening = pb;
        else if (data.StartDate >= month && data.StartDate <= last) opening = data.StartBalance;

        // Im Monat des Stichtags nur Buchungen ab dem Stichtag zählen – sonst passt
        // Anfang + Einnahmen − Ausgaben nicht zum Monatsende (frühere Buchungen stecken nicht im Kontostand)
        var countFrom = data.StartDate >= month && data.StartDate <= last ? data.StartDate : month;

        decimal income = 0, expense = 0, salary = 0;
        decimal? min = null;
        DateOnly? minDate = null;
        decimal? closing = null;
        for (var d = countFrom; d <= last; d = d.AddDays(1))
        {
            if (!days.TryGetValue(d, out var info)) continue;
            foreach (var i in info.Items)
            {
                if (i.Signed > 0) income += i.Signed; else expense -= i.Signed;
                if (i.IsSalary) salary += i.Signed;
            }
            if (info.Balance is { } b)
            {
                closing = b;
                if (min is null || b < min) { min = b; minDate = d; }
            }
        }

        return new MonthStats
        {
            Month = month, Opening = opening, Closing = closing, Min = min, MinDate = minDate,
            Income = income, Expense = expense, Salary = salary,
        };
    }
}

/// <summary>Regelmäßige Beträge, umgerechnet auf einen Monat (vierteljährlich/jährlich anteilig, ohne Einmalzahlungen).</summary>
public static class Recurring
{
    public static decimal PerMonth(IEnumerable<Entry> entries, DateOnly month) =>
        entries.Where(e => !e.IsOneTime && e.IsActiveInMonth(month)).Sum(e => e.Amount / e.IntervalMonths);

    /// <summary>Betrag im angegebenen Monat – oder, falls dort nichts läuft, im ersten späteren Monat (max. 24 Monate).</summary>
    public static (decimal Amount, DateOnly Month) CurrentOrNext(IEnumerable<Entry> entries, DateOnly month)
    {
        var list = entries.ToList();
        for (int i = 0; i < 24; i++)
        {
            var m = month.AddMonths(i);
            var v = PerMonth(list, m);
            if (v != 0) return (v, m);
        }
        return (0, month);
    }

    /// <summary>z. B. "+2.280,00 € / Monat" oder "+2.280,00 € / Monat ab Dez. 2026".</summary>
    public static string Describe(IEnumerable<Entry> entries, DateOnly month, string sign)
    {
        var (amount, from) = CurrentOrNext(entries, month);
        var text = sign + Money.Format(amount) + " / Monat";
        return from > month ? text + " ab " + from.ToDateTime(TimeOnly.MinValue).ToString("MMM yyyy", Money.De) : text;
    }
}