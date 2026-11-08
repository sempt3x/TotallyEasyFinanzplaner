using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner;

/// <summary>Liniendiagramm des täglichen Kontostands mit Nulllinie, Tiefststand-Markierung und Hover-Anzeige.</summary>
public sealed class BalanceChart : FrameworkElement
{
    private const double PadLeft = 78, PadRight = 18, PadTop = 14, PadBottom = 28;

    private IReadOnlyList<(DateOnly Date, decimal Value)> _points = [];
    private int _hover = -1;

    private static readonly Typeface Face = new("Segoe UI");

    // Farben kommen aus der aktiven Palette (hell/dunkel) und werden bei jedem Zeichnen neu gelesen.
    private Color Res(string key, Color fallback) => (TryFindResource(key) as SolidColorBrush)?.Color ?? fallback;
    private Brush PositiveLine => Frozen(Res("AccentBrush", Colors.SteelBlue));
    private Brush PositiveFill => Frozen(WithAlpha(Res("AccentBrush", Colors.SteelBlue), 0x2C));
    private Brush NegativeLine => Frozen(Res("ExpenseBrush", Colors.IndianRed));
    private Brush NegativeFill => Frozen(WithAlpha(Res("ExpenseBrush", Colors.IndianRed), 0x30));
    private Brush GridBrush => Frozen(Res("SubtleLineBrush", Colors.Gainsboro));
    private Brush LabelBrush => Frozen(Res("MutedBrush", Colors.Gray));
    private Brush TextBrush => Frozen(Res("TextBrush", Colors.Black));
    private Brush SurfaceBrush => Frozen(Res("SurfaceBrush", Colors.White));
    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public BalanceChart()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;
    }

    public void SetData(IEnumerable<(DateOnly Date, decimal Value)> points)
    {
        _points = points.ToList();
        _hover = -1;
        InvalidateVisual();
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / mag;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * mag;
    }

    private FormattedText Text(string s, double size, Brush brush, bool bold = false) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Face.FontFamily, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private double PlotWidth => Math.Max(1, ActualWidth - PadLeft - PadRight);
    private double PlotHeight => Math.Max(1, ActualHeight - PadTop - PadBottom);
    private double X(int i) => PadLeft + (_points.Count < 2 ? 0 : i * PlotWidth / (_points.Count - 1));

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // für Maus-Ereignisse

        if (_points.Count < 2)
        {
            var t = Text("Keine Prognosedaten – Startdatum liegt nach dem Zeitraum.", 12, LabelBrush);
            dc.DrawText(t, new Point((ActualWidth - t.Width) / 2, (ActualHeight - t.Height) / 2));
            return;
        }

        // Wertebereich inkl. Null mit etwas Luft; Gitterlinien auf "schönen" Werten
        double lo = Math.Min(0, (double)_points.Min(p => p.Value));
        double hi = Math.Max(0, (double)_points.Max(p => p.Value));
        if (hi - lo < 1) hi = lo + 100;
        double step = NiceStep((hi - lo) / 5);
        double pad = (hi - lo) * 0.06;
        lo -= lo < 0 ? pad : 0;
        hi += pad;
        double Y(double v) => PadTop + (hi - v) / (hi - lo) * PlotHeight;

        // Horizontale Gitterlinien + Beschriftung
        var gridPen = new Pen(GridBrush, 1);
        for (double raw = Math.Ceiling(lo / step) * step; raw <= hi; raw += step)
        {
            double v = Math.Abs(raw) < step * 1e-6 ? 0 : raw; // kein "-0 €"
            double y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(gridPen, new Point(PadLeft, y), new Point(PadLeft + PlotWidth, y));
            var label = Text(v.ToString("N0", Money.De) + " €", 11, LabelBrush);
            dc.DrawText(label, new Point(PadLeft - 10 - label.Width, y - label.Height / 2));
        }

        // Monatsbeschriftung
        // Erster Monatsanfang im Diagramm – Beschriftung am Start nur, wenn genug Platz bis dorthin ist
        int firstMonthStart = Enumerable.Range(1, _points.Count - 1).FirstOrDefault(k => _points[k].Date.Day == 1);
        bool labelStart = _points[0].Date.Day == 1 || firstMonthStart == 0 || X(firstMonthStart) - X(0) > 52;
        for (int i = 0; i < _points.Count; i++)
        {
            if (_points[i].Date.Day != 1 && i != 0) continue;
            if (i == 0 && !labelStart) continue;
            double x = Math.Round(X(i)) + 0.5;
            if (i != 0) dc.DrawLine(gridPen, new Point(x, PadTop), new Point(x, PadTop + PlotHeight));
            var label = Text(_points[i].Date.ToDateTime(TimeOnly.MinValue).ToString("MMM yy", Money.De), 11, LabelBrush);
            if (x + label.Width <= ActualWidth - 2)
                dc.DrawText(label, new Point(x + 4, PadTop + PlotHeight + 8));
        }

        // Linie und Fläche (über Null blau, unter Null rot)
        double zeroY = Y(0);
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(new Point(X(0), Y((double)_points[0].Value)), false, false);
            for (int i = 1; i < _points.Count; i++)
            {
                // Stufenverlauf: Kontostand ändert sich sprunghaft zum Buchungstag
                g.LineTo(new Point(X(i), Y((double)_points[i - 1].Value)), true, false);
                g.LineTo(new Point(X(i), Y((double)_points[i].Value)), true, false);
            }
        }
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(X(0), zeroY), true, true);
            g.LineTo(new Point(X(0), Y((double)_points[0].Value)), false, false);
            for (int i = 1; i < _points.Count; i++)
            {
                g.LineTo(new Point(X(i), Y((double)_points[i - 1].Value)), false, false);
                g.LineTo(new Point(X(i), Y((double)_points[i].Value)), false, false);
            }
            g.LineTo(new Point(X(_points.Count - 1), zeroY), false, false);
        }
        line.Freeze();
        area.Freeze();

        var above = new RectangleGeometry(new Rect(0, 0, ActualWidth, Math.Max(0, zeroY)));
        var below = new RectangleGeometry(new Rect(0, zeroY, ActualWidth, Math.Max(0, ActualHeight - zeroY)));
        dc.PushClip(above);
        dc.DrawGeometry(PositiveFill, null, area);
        dc.Pop();
        dc.PushClip(below);
        dc.DrawGeometry(NegativeFill, null, area);
        dc.DrawGeometry(null, new Pen(NegativeLine, 2) { LineJoin = PenLineJoin.Round }, line);
        dc.Pop();
        dc.PushClip(above);
        dc.DrawGeometry(null, new Pen(PositiveLine, 2) { LineJoin = PenLineJoin.Round }, line);
        dc.Pop();

        // Nulllinie
        if (lo < 0)
        {
            var zeroPen = new Pen(NegativeLine, 1) { DashStyle = new DashStyle([4, 3], 0) };
            dc.DrawLine(zeroPen, new Point(PadLeft, Math.Round(zeroY) + 0.5), new Point(PadLeft + PlotWidth, Math.Round(zeroY) + 0.5));
        }

        // Tiefststand markieren
        int minIdx = 0;
        for (int i = 1; i < _points.Count; i++) if (_points[i].Value < _points[minIdx].Value) minIdx = i;
        var minPt = new Point(X(minIdx), Y((double)_points[minIdx].Value));
        var minBrush = _points[minIdx].Value < 0 ? NegativeLine : PositiveLine;
        dc.DrawEllipse(SurfaceBrush, new Pen(minBrush, 2), minPt, 4, 4);
        if (_hover < 0)
        {
            var t = Text($"Tiefststand {Money.Format(_points[minIdx].Value)} am {_points[minIdx].Date:dd.MM.yyyy}", 11, minBrush, true);
            double tx = Math.Clamp(minPt.X - t.Width / 2, PadLeft + 6, ActualWidth - PadRight - t.Width);
            double ty = minPt.Y + 10 + t.Height > PadTop + PlotHeight ? minPt.Y - 10 - t.Height : minPt.Y + 10;
            dc.DrawRoundedRectangle(SurfaceBrush, null, new Rect(tx - 4, ty - 2, t.Width + 8, t.Height + 4), 3, 3);
            dc.DrawText(t, new Point(tx, ty));
        }

        // Hover: senkrechte Linie + Wertebox
        if (_hover >= 0 && _hover < _points.Count)
        {
            var (date, value) = _points[_hover];
            double x = X(_hover), y = Y((double)value);
            dc.DrawLine(new Pen(LabelBrush, 1) { DashStyle = new DashStyle([2, 2], 0) },
                new Point(x, PadTop), new Point(x, PadTop + PlotHeight));
            var b = value < 0 ? NegativeLine : PositiveLine;
            dc.DrawEllipse(b, new Pen(SurfaceBrush, 2), new Point(x, y), 5, 5);

            var t1 = Text(date.ToDateTime(TimeOnly.MinValue).ToString("ddd, dd.MM.yyyy", Money.De), 11, LabelBrush);
            var t2 = Text(Money.Format(value), 14, value < 0 ? NegativeLine : TextBrush, true);
            double w = Math.Max(t1.Width, t2.Width) + 20, h = t1.Height + t2.Height + 14;
            double bx = x + 12 + w > ActualWidth - 4 ? x - 12 - w : x + 12;
            double by = Math.Clamp(y - h / 2, PadTop, PadTop + PlotHeight - h);
            dc.DrawRoundedRectangle(SurfaceBrush, new Pen(LabelBrush, 0.5), new Rect(bx, by, w, h), 4, 4);
            dc.DrawText(t1, new Point(bx + 10, by + 6));
            dc.DrawText(t2, new Point(bx + 10, by + 8 + t1.Height));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_points.Count < 2) return;
        double x = e.GetPosition(this).X;
        int i = (int)Math.Round((x - PadLeft) / PlotWidth * (_points.Count - 1));
        i = Math.Clamp(i, 0, _points.Count - 1);
        if (i == _hover) return;
        _hover = i;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }
}
