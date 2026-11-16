using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner;

/// <summary>
/// Kleines Symbol für den Zahlungsweg einer Buchung (<see cref="PaymentMethod"/>): Bank-Symbol für das Konto
/// (Standard), Kartensymbol für die Kreditkarte, Uhr für Ratenkauf, farbige Kürzel-Plaketten für PayPal, Klarna,
/// Amazon, Apple Pay und Google Pay.
/// <code>&lt;local:PaymentIcon Method="{Binding Payment}" Size="14"/&gt;</code>
/// </summary>
public sealed class PaymentIcon : Border
{
    public static readonly DependencyProperty MethodProperty = DependencyProperty.Register(
        nameof(Method), typeof(PaymentMethod), typeof(PaymentIcon),
        new FrameworkPropertyMetadata(PaymentMethod.Konto, (d, _) => ((PaymentIcon)d).Build()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(PaymentIcon),
        new FrameworkPropertyMetadata(14.0, (d, _) => ((PaymentIcon)d).Build()));

    public PaymentMethod Method
    {
        get => (PaymentMethod)GetValue(MethodProperty);
        set => SetValue(MethodProperty, value);
    }

    /// <summary>Kantenlänge in Pixeln (Symbole sind quadratisch).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public PaymentIcon()
    {
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
        Build();
    }

    private static SolidColorBrush Brush(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }

    private void Build()
    {
        double s = Size;
        Width = Height = s;
        ToolTip = "Bezahlt über " + Texts.Of(Method);
        BorderThickness = new Thickness(0);
        ClearValue(BorderBrushProperty);
        ClearValue(BackgroundProperty);

        switch (Method)
        {
            case PaymentMethod.Kreditkarte: Glyph(""); break;                       // PaymentCard
            case PaymentMethod.PayPal: Badge("P", Brush(0xFF003087), Brush(0xFFFFFFFF), italic: true); break;
            case PaymentMethod.Klarna: Badge("K", Brush(0xFFFFB3C7), Brush(0xFF17120F)); break;
            case PaymentMethod.Amazon: Badge("a", Brush(0xFF232F3E), Brush(0xFFFF9900)); break;
            case PaymentMethod.Ratenkauf: Glyph(""); break;                         // Uhr: Zahlung in Raten
            case PaymentMethod.ApplePay: Badge("A", Brush(0xFF1D1D1F), Brush(0xFFFFFFFF)); break;
            case PaymentMethod.GooglePay:
                Badge("G", Brush(0xFFFFFFFF), Brush(0xFF4285F4));
                BorderBrush = Brush(0xFFDADCE0);
                BorderThickness = new Thickness(1);
                break;
            default: Glyph(""); break;                                              // Bank
        }
    }

    /// <summary>Einfarbiges Segoe-Fluent-Symbol in der gedämpften Textfarbe (hell/dunkel).</summary>
    private void Glyph(string glyph)
    {
        CornerRadius = new CornerRadius(0);
        var text = new TextBlock
        {
            Text = glyph,
            FontSize = Size * 0.95,
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Child = text;
    }

    /// <summary>Abgerundete Plakette mit Anfangsbuchstaben in den Markenfarben.</summary>
    private void Badge(string letter, Brush background, Brush foreground, bool italic = false)
    {
        CornerRadius = new CornerRadius(Size * 0.25);
        Background = background;
        Child = new TextBlock
        {
            Text = letter,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            FontSize = Size * 0.7,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, Size * 0.04),
        };
    }
}
