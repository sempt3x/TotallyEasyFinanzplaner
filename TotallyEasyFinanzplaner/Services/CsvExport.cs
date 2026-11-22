using System.Text;
using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner.Services;

/// <summary>CSV-Export (Excel, Semikolon, deutsches Zahlenformat): alle Buchungen und darunter die Kreditpläne.</summary>
public static class CsvExport
{
    public const string Header =
        "Art;Arbeitgeber/Job;Kategorie;Bezeichnung;Betrag;Rhythmus;Fällig;Gültig ab;Endet am;Wochenende;Notiz;Bezahlt über";

    /// <summary>
    /// Textfeld quoten. Beginnt ein Text mit = + - @ (oder Tab/Zeilenumbruch), würde Excel ihn als Formel ausführen
    /// (CSV-Injection) – ein vorangestelltes ' macht ihn zu reinem Text.
    /// </summary>
    public static string Quote(string? s)
    {
        s ??= "";
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n') s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static string Amount(decimal v) => v.ToString("0.00", Money.De);

    public static string Build(FinanceData data, DateOnly today)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        foreach (var x in data.Entries.OrderByDescending(x => x.Type).ThenBy(x => x.Job).ThenBy(x => x.IsOneTime).ThenBy(x => x.Name))
        {
            sb.AppendJoin(';',
                Quote(x.TypeText), Quote(x.IsSalary ? x.JobName : ""), Quote(x.Type == EntryType.Ausgabe ? x.CategoryName : ""), Quote(x.Name),
                Amount(x.SignedAmount), Quote(x.RecurrenceText), Quote(x.DueText), x.StartDate.ToString("dd.MM.yyyy"),
                x.EndDate?.ToString("dd.MM.yyyy") ?? "", Quote(Texts.Of(x.Weekend)), Quote(x.Note), Quote(x.PaymentText));
            sb.AppendLine();
        }

        // Kreditpläne: eine Zeile je Plan mit der monatlichen Rate; Eckdaten und Restschuld in der Notiz
        foreach (var l in data.Loans.OrderBy(l => l.Kind).ThenBy(l => l.Name))
        {
            var rows = LoanSchedule.Build(l);
            var last = rows.LastOrDefault(r => r.Kind != LoanRowKind.Auszahlung);
            var payoff = rows.FirstOrDefault(r => r.Kind == LoanRowKind.Abloesung);
            var details = new List<string>
            {
                $"Betrag {Money.Format(l.Amount)}",
                l.InterestRate > 0 ? $"Sollzins {l.InterestRate.ToString("0.###", Money.De)} %" : "zinsfrei",
                $"{rows.Count(r => r.Kind == LoanRowKind.Rate)} Raten",
                $"Restschuld am {today:dd.MM.yyyy} {Money.Format(LoanSchedule.RemainingAt(l, rows, today))}",
            };
            if (!string.IsNullOrWhiteSpace(l.Lender)) details.Insert(0, l.Lender.Trim());
            if (l.SpecialPayments.Count > 0)
                details.Add("Sondertilgungen: " + string.Join(", ", l.SpecialPayments.OrderBy(p => p.Date)
                    .Select(p => $"{p.Date:dd.MM.yyyy} {Money.Format(p.Amount)}")));
            if (payoff is not null) details.Add($"Ablösung am {payoff.Nominal:dd.MM.yyyy} {Money.Format(payoff.Payment)}");
            if (l.Kind == LoanKind.Kredit && l.PayoutDate is { } po) details.Add($"Auszahlung am {po:dd.MM.yyyy}");
            if (!string.IsNullOrWhiteSpace(l.Note)) details.Add(l.Note.Trim());

            sb.AppendJoin(';',
                Quote(Texts.Of(l.Kind)), Quote(""), Quote("Kredite & Finanzierung"), Quote(l.Name),
                Amount(-l.Rate), Quote("Monatlich"), Quote(Texts.Day(l.FirstRateDate.Day)), l.FirstRateDate.ToString("dd.MM.yyyy"),
                last?.Nominal.ToString("dd.MM.yyyy") ?? "", Quote(Texts.Of(l.Weekend)), Quote(string.Join(" · ", details)),
                Quote(Texts.Of(l.PaymentKind)));
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
