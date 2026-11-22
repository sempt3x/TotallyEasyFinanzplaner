using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TotallyEasyFinanzplaner.Models;
using TotallyEasyFinanzplaner.Services;

namespace TotallyEasyFinanzplaner;

/// <summary>Seite "Kredite &amp; Raten": Kreditpläne und Ratenzahlungen, Tilgungsplan, Sondertilgung und Ablösung.</summary>
public partial class MainWindow
{
    private Guid? _selectedLoanId;
    /// <summary>Gesetzt, während die Liste neu aufgebaut wird (Auswahländerung dabei ignorieren).</summary>
    private bool _refreshingLoans;

    private Loan? SelectedLoan => _data.Loans.FirstOrDefault(l => l.Id == _selectedLoanId);

    /// <summary>Liste, Summen und Tilgungsplan neu aufbauen (Teil von <see cref="RefreshAll"/>).</summary>
    private void RefreshLoans()
    {
        var today = Today;
        var rows = _data.Loans
            .Select(l => new LoanListRow(l, today))
            .OrderBy(r => r.IsFinished)
            .ThenBy(r => r.LastPayment ?? DateOnly.MaxValue)
            .ToList();

        _refreshingLoans = true;
        LoanGrid.ItemsSource = rows;
        LoanGrid.SelectedItem = rows.FirstOrDefault(r => r.Loan.Id == _selectedLoanId);
        _refreshingLoans = false;
        LoansEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        int credits = rows.Count(r => r.Loan.Kind == LoanKind.Kredit);
        int open = rows.Count(r => !r.IsFinished);
        LoansCount.Text = $"{credits} Kredit{(credits == 1 ? "" : "e")} · {rows.Count - credits} Ratenzahlung{(rows.Count - credits == 1 ? "" : "en")}" +
                          $" · {open} laufend";

        var month = FirstOfMonth(today);
        LoansTotal.Text = "Restschuld " + Money.Format(rows.Where(r => !r.IsFinished).Sum(r => r.Remaining));
        LoansTotalSub.Text = $"Raten {ToDateTime(month).ToString("MMMM yyyy", Money.De)}: −{Money.Plain(Recurring.LoanRates(_data.Loans, month))} €";

        ShowLoanDetail(rows.FirstOrDefault(r => r.Loan.Id == _selectedLoanId));
    }

    private void ShowLoanDetail(LoanListRow? row)
    {
        LoanDetail.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        LoanDetailEmpty.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        RemoveSpecialButton.IsEnabled = false;
        if (row is null)
        {
            ScheduleGrid.ItemsSource = null;
            return;
        }

        var loan = row.Loan;
        var today = Today;
        LoanDetailGlyph.Text = row.KindGlyph;
        LoanDetailTitle.Text = loan.Name;
        LoanDetailSub.Text = $"{row.SubText} · {Money.Format(loan.Amount)} · Rate {Money.Format(loan.Rate)} ab {loan.FirstRateDate:dd.MM.yyyy}" +
                             (loan.Payment is null ? "" : " · über " + Texts.Of(loan.PaymentKind));

        var payments = row.Rows.Where(r => r.Kind != LoanRowKind.Auszahlung).ToList();
        ShowAmount(LoanStatRemaining, row.IsFinished ? 0 : row.Remaining);
        LoanStatPaid.Text = Money.Format(loan.Amount - row.Remaining);
        LoanStatInterest.Text = loan.InterestRate > 0 ? Money.Format(payments.Sum(r => r.Interest)) : "zinsfrei";
        LoanStatEndLabel.Text = loan.PayoffDate is null ? "LETZTE ZAHLUNG" : "ABLÖSUNG AM";
        LoanStatEnd.Text = row.LastPayment is { } last ? last.ToString("dd.MM.yyyy") : "–";

        // Vorzeitige Ablösung: Hinweis mit Betrag, Button heißt dann "Ablösung ändern"
        var payoff = payments.LastOrDefault(r => r.Kind == LoanRowKind.Abloesung);
        PayoffButton.Content = payoff is null ? "Vorzeitig ablösen" : "Ablösung ändern";
        LoanPayoffBox.Visibility = payoff is null ? Visibility.Collapsed : Visibility.Visible;
        if (payoff is not null)
            LoanPayoffText.Text = $"Vorzeitige Ablösung am {payoff.Nominal:dd.MM.yyyy}: Restzahlung {Money.Format(payoff.Payment)}" +
                                  (loan.PayoffAmount is null ? " (berechnet)" : " (Betrag laut Bank)") + " – danach keine weiteren Raten.";

        ScheduleGrid.ItemsSource = row.Rows.Select(r => new LoanScheduleRow(loan, r, today)).ToList();
        // Zur nächsten offenen Zahlung scrollen
        var next = ((List<LoanScheduleRow>)ScheduleGrid.ItemsSource).FirstOrDefault(r => !r.IsFinished);
        if (next is not null) ScheduleGrid.ScrollIntoView(next);
    }

    private void LoanGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingLoans || !ReferenceEquals(e.OriginalSource, sender)) return;
        var row = LoanGrid.SelectedItem as LoanListRow;
        _selectedLoanId = row?.Loan.Id;
        ShowLoanDetail(row);
    }

    /// <summary>Kreditplan auswählen und die Seite "Kredite &amp; Raten" zeigen (z. B. aus dem Kalender).</summary>
    private void ShowLoan(Loan loan)
    {
        _selectedLoanId = loan.Id;
        RefreshLoans();
        ShowPage(NavLoans);
    }

    private void NewLoan_Click(object sender, RoutedEventArgs e)
    {
        if (_data.Loans.Count >= Limits.MaxLoans)
        {
            MessageDialog.Show(this, $"Ein Finanzplan kann höchstens {Limits.MaxLoans} Kreditpläne enthalten.\n\n" +
                                     "Bitte nicht mehr benötigte (getilgte) Kreditpläne löschen.", "Obergrenze erreicht",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var kind = (sender as FrameworkElement)?.Tag as string == nameof(LoanKind.Ratenzahlung) ? LoanKind.Ratenzahlung : LoanKind.Kredit;
        // Erste Rate: nächster Monatsanfang
        var firstRate = FirstOfMonth(Today).AddMonths(1);
        var loan = new Loan
        {
            Kind = kind,
            FirstRateDate = firstRate,
            Payment = kind == LoanKind.Ratenzahlung ? PaymentMethod.Ratenkauf : null,
        };
        if (new LoanDialog(loan, isNew: true) { Owner = this }.ShowDialog() != true) return;
        _data.Loans.Add(loan);
        _selectedLoanId = loan.Id;
        SaveAndRefresh();
    }

    private void EditLoan_Click(object sender, RoutedEventArgs e)
    {
        // Doppelklick auf den Spaltenkopf o. Ä. ignorieren
        if (e is MouseButtonEventArgs { OriginalSource: DependencyObject src } && FindParent<DataGridRow>(src) is null) return;
        if (SelectedLoan is not { } loan) return;
        if (new LoanDialog(loan, isNew: false) { Owner = this }.ShowDialog() == true) SaveAndRefresh();
    }

    private void DeleteLoan_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan is not { } loan) return;
        if (MessageDialog.Show(this,
                $"{Texts.Of(loan.Kind)} „{loan.Name}“ mit allen Raten, Sondertilgungen und der Ablösung löschen?\n\n" +
                "Die Zahlungen verschwinden dann auch aus Kalender und Prognose.",
                "Löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _data.Loans.Remove(loan);
        _selectedLoanId = null;
        SaveAndRefresh();
    }

    private void AddSpecialPayment_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan is not { } loan) return;
        if (loan.SpecialPayments.Count >= Limits.MaxSpecialPayments)
        {
            MessageDialog.Show(this, $"Ein Kreditplan kann höchstens {Limits.MaxSpecialPayments} Sondertilgungen enthalten.",
                "Obergrenze erreicht", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var payment = new LoanPayment();
        if (LoanPaymentDialog.ForSpecialPayment(loan, payment, isNew: true).ShowOwned(this) != true) return;
        loan.SpecialPayments.Add(payment);
        SaveAndRefresh();
    }

    private void Payoff_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan is not { } loan) return;
        if (LoanPaymentDialog.ForPayoff(loan).ShowOwned(this) == true) SaveAndRefresh();
    }

    private void RemovePayoff_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan is not { } loan) return;
        loan.PayoffDate = null;
        loan.PayoffAmount = null;
        SaveAndRefresh();
    }

    /// <summary>Im Tilgungsplan ausgewählte Sondertilgung (gehört zum Kreditplan <paramref name="loan"/>).</summary>
    private LoanPayment? SelectedSpecialPayment(Loan loan) =>
        ScheduleGrid.SelectedItem is LoanScheduleRow { Row.Special: { } p } && loan.SpecialPayments.Contains(p) ? p : null;

    private void ScheduleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveSpecialButton.IsEnabled = SelectedLoan is { } loan && SelectedSpecialPayment(loan) is not null;
    }

    private void ScheduleGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedLoan is not { } loan) return;
        if (ScheduleGrid.SelectedItem is LoanScheduleRow { Row.Kind: LoanRowKind.Abloesung })
        {
            Payoff_Click(sender, e);
            return;
        }
        if (SelectedSpecialPayment(loan) is not { } payment) return;
        if (LoanPaymentDialog.ForSpecialPayment(loan, payment, isNew: false).ShowOwned(this) == true) SaveAndRefresh();
    }

    private void RemoveSpecialPayment_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan is not { } loan || SelectedSpecialPayment(loan) is not { } payment) return;
        if (MessageDialog.Show(this, $"Sondertilgung über {Money.Format(payment.Amount)} am {payment.Date:dd.MM.yyyy} entfernen?",
                "Sondertilgung entfernen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        loan.SpecialPayments.Remove(payment);
        SaveAndRefresh();
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T)
            d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}
