using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner;

/// <summary>Zeile in der Liste "Kredite &amp; Ratenzahlungen": Kreditplan mit Stand von heute.</summary>
public sealed class LoanListRow
{
    public LoanListRow(Loan loan, DateOnly today)
    {
        Loan = loan;
        Rows = LoanSchedule.Build(loan);
        Remaining = LoanSchedule.RemainingAt(loan, Rows, today);
        var payments = Rows.Where(r => r.Kind != LoanRowKind.Auszahlung).ToList();
        LastPayment = payments.Count > 0 ? payments[^1].Nominal : null;
        NextPayment = payments.FirstOrDefault(r => r.Nominal >= today);
        IsFinished = LastPayment is { } last && last < today;
    }

    public Loan Loan { get; }
    public List<LoanRow> Rows { get; }
    public decimal Remaining { get; }
    public DateOnly? LastPayment { get; }
    public LoanRow? NextPayment { get; }
    /// <summary>Vollständig zurückgezahlt (wird blass dargestellt).</summary>
    public bool IsFinished { get; }

    public string Name => Loan.Name;
    public PaymentMethod Payment => Loan.PaymentKind;
    public string KindGlyph => Loan.Kind == LoanKind.Kredit ? "" : "";
    /// <summary>z. B. "Kredit · Sparkasse · 5,9 %".</summary>
    public string SubText => Texts.Of(Loan.Kind)
        + (string.IsNullOrWhiteSpace(Loan.Lender) ? "" : " · " + Loan.Lender.Trim())
        + (Loan.InterestRate > 0 ? $" · {Loan.InterestRate.ToString("0.###", Money.De)} %" : " · zinsfrei");
    public string RateText => "−" + Money.Format(Loan.Rate);
    public string RemainingText => IsFinished ? "getilgt" : Money.Format(Remaining);
    public string EndText => LastPayment is { } d ? d.ToString("MM.yyyy") : "–";
    /// <summary>Anteil bereits getilgt (0–1) für den Fortschrittsbalken.</summary>
    public double Progress => Loan.Amount <= 0 ? 0 : (double)Math.Clamp(1 - Remaining / Loan.Amount, 0, 1);
}

/// <summary>Zeile im Tilgungsplan.</summary>
public sealed class LoanScheduleRow(Loan loan, LoanRow row, DateOnly today)
{
    public LoanRow Row { get; } = row;
    /// <summary>Tatsächlicher Buchungstag (nach Wochenend-/Feiertagsregel).</summary>
    public DateOnly Date { get; } = new Entry { Weekend = loan.Weekend }.ApplyWeekendRule(row.Nominal);
    public bool IsFinished => Date < today;
    public bool IsPayout => Row.Kind == LoanRowKind.Auszahlung;
    public bool IsSpecial => Row.Kind is LoanRowKind.Sondertilgung or LoanRowKind.Abloesung;

    public string NumberText => Row.Number > 0 ? Row.Number.ToString() : "";
    public string DateText => Date.ToString("dd.MM.yyyy");
    public string KindText => Texts.Of(Row.Kind) + (string.IsNullOrWhiteSpace(Row.Note) ? "" : " · " + Row.Note);
    public string PaymentText => (IsPayout ? "+" : "−") + Money.Format(Row.Payment);
    public string InterestText => IsPayout || Row.Interest == 0 ? "" : Money.Format(Row.Interest);
    public string PrincipalText => IsPayout ? "" : Money.Format(Row.Principal);
    public string RemainingText => Money.Format(Row.Remaining);
}

public static class DialogExtensions
{
    /// <summary>Dialog über <paramref name="owner"/> modal anzeigen.</summary>
    public static bool? ShowOwned(this System.Windows.Window dialog, System.Windows.Window owner)
    {
        dialog.Owner = owner;
        return dialog.ShowDialog();
    }
}
