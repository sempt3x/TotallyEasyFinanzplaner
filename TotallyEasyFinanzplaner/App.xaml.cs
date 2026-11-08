using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace TotallyEasyFinanzplaner;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = de;
        CultureInfo.DefaultThreadCurrentUICulture = de;
        Thread.CurrentThread.CurrentCulture = de;
        Thread.CurrentThread.CurrentUICulture = de;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(de.IetfLanguageTag)));

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                MessageDialog.Show(null, args.Exception.Message, "Unerwarteter Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // Falls das eigene Meldungsfenster selbst scheitert: Standardmeldung von Windows
                MessageBox.Show(args.Exception.Message, "Unerwarteter Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        // Während der Passwortabfrage gibt es noch kein Hauptfenster – nicht beim Schließen des Dialogs beenden
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Services.ThemeManager.Initialize();
        base.OnStartup(e);

        try
        {
            var doc = PlanOpener.OpenAtStartup(e.Args.FirstOrDefault());
            if (doc is null)
            {
                Shutdown();
                return;
            }

            var main = new MainWindow(doc);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            // Ohne Hauptfenster würde das Programm sonst unsichtbar weiterlaufen (OnExplicitShutdown)
            try
            {
                MessageDialog.Show(null, ex.Message, "Programmstart fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
                MessageBox.Show(ex.Message, "Programmstart fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(1);
        }
    }
}
