using System.Windows;
using System.Windows.Controls;
using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner;

/// <summary>
/// Sondertilgung eintragen bzw. ändern – oder den Kredit vorzeitig ablösen. Ändert den Kreditplan erst bei "Speichern".
/// </summary>
public partial class LoanPaymentDialog : Window
{
    private readonly Loan _loan;
    private readonly LoanPayment? _special; // null = Ablösung
    private bool _ready;

    private bool IsPayoff => _special is null;

    /// <summary>Sondertilgung anlegen (<paramref name="isNew"/>) oder ändern.</summary>
    public static LoanPaymentDialog ForSpecialPayment(Loan loan, LoanPayment payment, bool isNew) => new(loan, payment, isNew);

    /// <summary>Vorzeitige Ablösung festlegen bzw. ändern.</summary>
    public static LoanPaymentDialog ForPayoff(Loan loan) => new(loan, null, loan.PayoffDate is null);

    private LoanPaymentDialog(Loan loan, LoanPayment? special, bool isNew)
    {
        InitializeComponent();
        _loan = loan;
        _special = special;

        HeaderText.Text = IsPayoff ? "Vorzeitig ablösen" : isNew ? "Sondertilgung" : "Sondertilgung ändern";
        Title = HeaderText.Text;
        SubText.Text = $"{Texts.Of(loan.Kind)} „{loan.Name}“";
        HeaderGlyph.Text = IsPayoff ? "" : "";
        AmountLabel.Text = IsPayoff ? "Betrag laut Bank (€)" : "Betrag (€)";
        AmountBox.ToolTip = IsPayoff
            ? "Leer lassen, um die berechnete Restzahlung zu verwenden. Nennt die Bank einen anderen Betrag (z. B. mit Vorfälligkeitsentschädigung), diesen eintragen."
            : "Zusätzliche Zahlung, die die Restschuld sofort verringert";
        NoteLabel.Visibility = NoteBox.Visibility = IsPayoff ? Visibility.Collapsed : Visibility.Visible;

        var today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly date = IsPayoff ? loan.PayoffDate ?? Later(today, Start(loan)) : isNew ? Later(today, Start(loan)) : special!.Date;
        DatePicker.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        AmountBox.Text = IsPayoff
            ? loan.PayoffAmount is { } pa ? Money.Plain(pa) : ""
            : isNew ? "" : Money.Plain(special!.Amount);
        NoteBox.Text = special?.Note ?? "";

        _ready = true;
        UpdateUi();
        Loaded += (_, _) => (IsPayoff ? (Control)DatePicker : AmountBox).Focus();
    }

    private static DateOnly Later(DateOnly a, DateOnly b) => a > b ? a : b;

    /// <summary>Frühestes Datum: Auszahlung bzw. erste Rate.</summary>
    private static DateOnly Start(Loan loan) => loan.PayoutDate ?? loan.FirstRateDate;

    /// <summary>Kreditplan ohne die bearbeitete Zahlung (bzw. ohne Ablösung) – Grundlage für Restschuld und Laufzeit.</summary>
    private Loan Baseline()
    {
        var copy = _loan.Copy();
        if (IsPayoff) { copy.PayoffDate = null; copy.PayoffAmount = null; }
        else copy.SpecialPayments.Remove(_special!);
        return copy;
    }

    private DateOnly? SelectedDate => DatePicker.SelectedDate is { } d && Limits.IsValidDate(d) ? DateOnly.FromDateTime(d) : null;

    /// <summary>Restschuld unmittelbar vor <paramref name="date"/> und letzter planmäßiger Zahlungstag.</summary>
    private (decimal Remaining, DateOnly LastPayment) Situation(DateOnly date)
    {
        var baseline = Baseline();
        var rows = LoanSchedule.Build(baseline);
        var last = rows.LastOrDefault(r => r.Kind != LoanRowKind.Auszahlung)?.Nominal ?? baseline.FirstRateDate;
        return (LoanSchedule.RemainingAt(baseline, rows, date.AddDays(-1)), last);
    }

    /// <summary>Berechnete Restzahlung bei Ablösung an <paramref name="date"/> (Restschuld + Zinsen seit der letzten Rate).</summary>
    private decimal ComputedPayoff(DateOnly date)
    {
        var probe = Baseline();
        probe.PayoffDate = date;
        return LoanSchedule.Build(probe).LastOrDefault(r => r.Kind == LoanRowKind.Abloesung)?.Payment ?? 0;
    }

    private void UpdateUi()
    {
        if (!_ready) return;
        if (SelectedDate is not { } date)
        {
            HintText.Text = "Bitte ein Datum wählen.";
            return;
        }

        var (remaining, last) = Situation(date);
        var shifted = new Entry { Weekend = _loan.Weekend }.ApplyWeekendRule(date);
        var shiftText = shifted != date
            ? $"\n{BusinessDays.Reason(date)}: gebucht wird am {shifted.ToDateTime(TimeOnly.MinValue).ToString("dddd, dd.MM.yyyy", Money.De)}."
            : "";

        if (IsPayoff)
        {
            var computed = ComputedPayoff(date);
            HintText.Text = $"Am {date:dd.MM.yyyy} wird die Restschuld auf einmal gezahlt, danach fallen keine Raten mehr an.\n" +
                            $"Berechnete Restzahlung: {Money.Format(computed)} (Restschuld {Money.Format(remaining)}" +
                            (computed > remaining ? $" + Zinsen seit der letzten Rate {Money.Format(computed - remaining)}" : "") + ").\n" +
                            "Weicht der Betrag der Bank ab (z. B. Vorfälligkeitsentschädigung), oben eintragen." + shiftText;
        }
        else
        {
            var text = $"Restschuld vor diesem Tag: {Money.Format(remaining)}. Die Rate bleibt gleich – der Kredit ist dadurch früher getilgt.";
            if (Money.TryParse(AmountBox.Text, out var amount) && amount > 0 && amount <= remaining)
            {
                var probe = Baseline();
                probe.SpecialPayments.Add(new LoanPayment { Date = date, Amount = amount });
                var end = LoanSchedule.Build(probe).LastOrDefault(r => r.Kind != LoanRowKind.Auszahlung)?.Nominal;
                if (end is { } e && e < last)
                    text += $"\nLetzte Zahlung dann am {e:dd.MM.yyyy} statt am {last:dd.MM.yyyy}.";
            }
            HintText.Text = text + shiftText;
        }
    }

    private void Date_Changed(object? sender, SelectionChangedEventArgs e) => UpdateUi();
    private void Amount_Changed(object sender, TextChangedEventArgs e) => UpdateUi();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? error = null;
        decimal amount = 0;
        bool hasAmount = !string.IsNullOrWhiteSpace(AmountBox.Text);

        if (SelectedDate is not { } date) error = $"Bitte ein Datum zwischen {Limits.DateRangeText} wählen.";
        else if (hasAmount && (!Money.TryParse(AmountBox.Text, out amount) || amount <= 0))
            error = "Bitte einen positiven Betrag eingeben (z. B. 1.000).";
        else if (!IsPayoff && !hasAmount) error = "Bitte den Betrag der Sondertilgung eingeben.";
        else
        {
            var (remaining, last) = Situation(date);
            var start = Start(_loan);
            if (date < start) error = $"Das Datum liegt vor dem Beginn ({start:dd.MM.yyyy}).";
            else if (date > last) error = $"Am {date:dd.MM.yyyy} ist bereits alles zurückgezahlt (letzte Zahlung am {last:dd.MM.yyyy}).";
            else if (!IsPayoff && _loan.PayoffDate is { } off && date >= off)
                error = $"Der Kredit wird am {off:dd.MM.yyyy} abgelöst – danach sind keine Sondertilgungen mehr möglich.";
            else if (!IsPayoff && amount > remaining)
                error = $"Die Sondertilgung ist höher als die Restschuld ({Money.Format(remaining)}). " +
                        "Um alles auf einmal zurückzuzahlen, „Vorzeitig ablösen“ verwenden.";
            else if (IsPayoff && _loan.SpecialPayments.Any(p => p.Date >= date))
                error = "Nach diesem Datum sind noch Sondertilgungen eingetragen. Bitte diese zuerst entfernen oder ein späteres Datum wählen.";

            if (error is null)
            {
                if (IsPayoff)
                {
                    _loan.PayoffDate = date;
                    _loan.PayoffAmount = hasAmount ? amount : null;
                }
                else
                {
                    _special!.Date = date;
                    _special.Amount = amount;
                    _special.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
                }
                DialogResult = true;
                return;
            }
        }

        MessageDialog.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
