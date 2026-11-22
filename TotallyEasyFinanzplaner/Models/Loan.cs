namespace TotallyEasyFinanzplaner.Models;

/// <summary>Kredit (Geld wird ausgezahlt) oder Ratenzahlung (Kauf in Raten, z. B. über Klarna oder den Händler).</summary>
public enum LoanKind { Kredit, Ratenzahlung }

/// <summary>Sondertilgung: zusätzliche Zahlung, die die Restschuld sofort verringert.</summary>
public sealed class LoanPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Kreditplan bzw. Ratenzahlung: Betrag, Zinssatz und monatliche Rate. Daraus ergibt sich der Tilgungsplan
/// (<see cref="LoanSchedule"/>), dessen Zahlungen automatisch in Kalender und Prognose gebucht werden.
/// Optional: Sondertilgungen und eine vorzeitige Ablösung (Restschuld an einem Tag auf einmal).
/// </summary>
public sealed class Loan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public LoanKind Kind { get; set; } = LoanKind.Kredit;
    /// <summary>Bank bzw. Händler / Anbieter (nur Anzeige).</summary>
    public string? Lender { get; set; }
    /// <summary>Kreditbetrag bzw. Kaufpreis (finanzierter Betrag).</summary>
    public decimal Amount { get; set; }
    /// <summary>Sollzins in Prozent pro Jahr; 0 = zinsfrei (z. B. 0-%-Finanzierung).</summary>
    public decimal InterestRate { get; set; }
    /// <summary>Monatliche Rate (Zins + Tilgung). Die letzte Rate ist ggf. kleiner.</summary>
    public decimal Rate { get; set; }
    /// <summary>Termin der ersten Rate; alle weiteren Raten folgen monatlich am selben Tag (bzw. am Monatsende).</summary>
    public DateOnly FirstRateDate { get; set; }
    public WeekendRule Weekend { get; set; } = WeekendRule.Verschieben;
    /// <summary>Worüber die Raten gezahlt werden. Null = Bankkonto.</summary>
    public PaymentMethod? Payment { get; set; }
    /// <summary>Nur Kredit: An diesem Tag wird der Kreditbetrag als Einnahme aufs Konto gebucht. Null = nicht buchen.</summary>
    public DateOnly? PayoutDate { get; set; }
    public List<LoanPayment> SpecialPayments { get; set; } = [];
    /// <summary>Vorzeitige Ablösung: An diesem Tag wird die Restschuld auf einmal gezahlt; danach keine Raten mehr.</summary>
    public DateOnly? PayoffDate { get; set; }
    /// <summary>Ablösebetrag laut Bank (inkl. Zinsen/Gebühren). Null = berechnete Restschuld samt anteiliger Zinsen.</summary>
    public decimal? PayoffAmount { get; set; }
    public string? Note { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public PaymentMethod PaymentKind => Payment ?? PaymentMethod.Konto;

    /// <summary>Kopie für Vorschau-Berechnungen (eigene Liste der Sondertilgungen).</summary>
    public Loan Copy()
    {
        var copy = (Loan)MemberwiseClone();
        copy.SpecialPayments = [.. SpecialPayments];
        return copy;
    }
}

public enum LoanRowKind { Auszahlung, Rate, Sondertilgung, Abloesung }

/// <summary>Eine Zeile im Tilgungsplan.</summary>
/// <param name="Nominal">Planmäßiger Termin (vor Verschiebung wegen Wochenende/Feiertag).</param>
/// <param name="Number">Nummer der Rate (1, 2 …); 0 bei anderen Zahlungen.</param>
/// <param name="Special">Bei Sondertilgungen: der zugehörige Eintrag in <see cref="Loan.SpecialPayments"/>.</param>
public sealed record LoanRow(LoanRowKind Kind, int Number, DateOnly Nominal, decimal Payment, decimal Interest,
                             decimal Principal, decimal Remaining, string? Note = null, LoanPayment? Special = null);

/// <summary>Berechnung des Tilgungsplans.</summary>
/// <remarks>
/// Zinsen werden monatlich auf die Restschuld berechnet (Sollzins / 12) und mit jeder Rate gezahlt; der Rest der Rate
/// tilgt. Sondertilgungen verringern die Restschuld sofort, die Rate bleibt gleich (der Kredit endet früher).
/// Bei der Ablösung werden die Zinsen seit der letzten Rate tagesgenau hinzugerechnet. Banken rechnen teils
/// abweichend – deshalb kann der Ablösebetrag laut Bank eingetragen werden.
/// </remarks>
public static class LoanSchedule
{
    /// <summary>Höchstzahl Raten (50 Jahre) – schützt vor endlosen Plänen.</summary>
    public const int MaxRates = 600;

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Monatszins in Euro auf <paramref name="remaining"/>.</summary>
    public static decimal MonthlyInterest(decimal remaining, decimal ratePercent) => Round(remaining * ratePercent / 1200m);

    /// <summary>Rate-Termin Nummer <paramref name="index"/> (0 = erste Rate): gleicher Tag, bei kürzeren Monaten der Monatsletzte.</summary>
    public static DateOnly RateDate(Loan loan, int index)
    {
        var first = loan.FirstRateDate;
        var month = new DateOnly(first.Year, first.Month, 1).AddMonths(index);
        return new DateOnly(month.Year, month.Month, Math.Min(first.Day, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    /// <summary>
    /// Monatliche Rate, mit der <paramref name="amount"/> in <paramref name="count"/> Raten zurückgezahlt ist
    /// (Annuität; zinsfrei: Betrag / Anzahl, auf den Cent aufgerundet).
    /// </summary>
    public static decimal RateFor(decimal amount, decimal ratePercent, int count)
    {
        if (count <= 0 || amount <= 0) return 0;
        if (ratePercent <= 0) return Math.Ceiling(amount / count * 100m) / 100m;
        double i = (double)ratePercent / 1200.0;
        double rate = (double)amount * i / (1 - Math.Pow(1 + i, -count));
        return Math.Ceiling((decimal)rate * 100m) / 100m;
    }

    /// <summary>Tilgungsplan: Auszahlung (falls gebucht), Raten, Sondertilgungen und ggf. Ablösung – nach Datum.</summary>
    public static List<LoanRow> Build(Loan loan)
    {
        var rows = new List<LoanRow>();
        if (loan.Amount <= 0 || loan.Rate <= 0) return rows;

        if (loan.Kind == LoanKind.Kredit && loan.PayoutDate is { } payout)
            rows.Add(new LoanRow(LoanRowKind.Auszahlung, 0, payout, loan.Amount, 0, 0, loan.Amount));

        decimal remaining = loan.Amount;
        var specials = loan.SpecialPayments.Where(p => p.Amount > 0).OrderBy(p => p.Date).ToList();
        int nextSpecial = 0;
        // Ab hier laufen Zinsen (für die tagesgenaue Ablösung): Auszahlung bzw. ein Monat vor der ersten Rate
        var interestFrom = loan.PayoutDate ?? RateDate(loan, -1);

        // Sondertilgungen bis einschließlich "bis" anwenden; true, wenn der Kredit danach getilgt ist
        bool ApplySpecials(DateOnly until)
        {
            while (nextSpecial < specials.Count && specials[nextSpecial].Date <= until)
            {
                var p = specials[nextSpecial++];
                if (loan.PayoffDate is { } off && p.Date >= off) continue; // nach der Ablösung bedeutungslos
                var pay = Math.Min(p.Amount, remaining);
                remaining -= pay;
                rows.Add(new LoanRow(LoanRowKind.Sondertilgung, 0, p.Date, pay, 0, pay, remaining, p.Note, p));
                if (remaining <= 0) return true;
            }
            return false;
        }

        for (int k = 0; k < MaxRates && remaining > 0; k++)
        {
            var due = RateDate(loan, k);

            if (loan.PayoffDate is { } payoffDate && payoffDate <= due)
            {
                if (ApplySpecials(payoffDate.AddDays(-1))) return rows;
                // Restschuld + Zinsen seit der letzten Rate (tagesgenau, 365 Tage), sofern kein Betrag der Bank vorliegt
                int days = Math.Max(0, payoffDate.DayNumber - interestFrom.DayNumber);
                var accrued = Round(remaining * loan.InterestRate / 100m * days / 365m);
                var total = loan.PayoffAmount ?? remaining + accrued;
                rows.Add(new LoanRow(LoanRowKind.Abloesung, 0, payoffDate, total, Math.Max(0, total - remaining), remaining, 0));
                return rows;
            }

            if (ApplySpecials(due.AddDays(-1))) return rows;

            var interest = MonthlyInterest(remaining, loan.InterestRate);
            var payment = Math.Min(loan.Rate, remaining + interest);
            var principal = payment - interest;
            if (principal <= 0) break; // Rate deckt die Zinsen nicht – wird im Dialog verhindert
            remaining -= principal;
            rows.Add(new LoanRow(LoanRowKind.Rate, k + 1, due, payment, interest, principal, remaining));
            interestFrom = due;
        }

        // Sondertilgungen nach der letzten Rate verfallen (Kredit ist bereits getilgt)
        return rows;
    }

    /// <summary>
    /// Die Zahlungen des Tilgungsplans als Buchungen für Kalender und Prognose. Sie werden nicht gespeichert, sondern
    /// bei jeder Berechnung aus dem Kreditplan erzeugt und verweisen über <see cref="Entry.Loan"/> auf ihn.
    /// </summary>
    public static IEnumerable<Entry> ToEntries(Loan loan)
    {
        var rows = Build(loan);
        int count = rows.Count(r => r.Kind == LoanRowKind.Rate);
        foreach (var r in rows)
        {
            bool payout = r.Kind == LoanRowKind.Auszahlung;
            var what = r.Kind switch
            {
                LoanRowKind.Auszahlung => "Auszahlung",
                LoanRowKind.Rate => $"Rate {r.Number}/{count}",
                LoanRowKind.Sondertilgung => "Sondertilgung",
                _ => "Ablösung (Restzahlung)",
            };
            yield return new Entry
            {
                Id = loan.Id,
                Name = $"{loan.Name} · {what}",
                Type = payout ? EntryType.Einnahme : EntryType.Ausgabe,
                Recurrence = Recurrence.Einmalig,
                StartDate = r.Nominal,
                Amount = r.Payment,
                Weekend = loan.Weekend,
                Category = payout ? null : "Kredite & Finanzierung",
                Payment = payout ? null : loan.Payment,
                Note = payout ? null : $"Restschuld danach {Money.Format(r.Remaining)}",
                Loan = loan,
                LoanRow = r,
            };
        }
    }

    /// <summary>Restschuld nach allen Zahlungen bis einschließlich <paramref name="date"/>.</summary>
    public static decimal RemainingAt(Loan loan, IReadOnlyList<LoanRow> rows, DateOnly date)
    {
        decimal remaining = loan.Amount;
        foreach (var r in rows)
        {
            if (r.Kind == LoanRowKind.Auszahlung) continue; // ändert die Restschuld nicht
            if (r.Nominal > date) break;
            remaining = r.Remaining;
        }
        return remaining;
    }
}
