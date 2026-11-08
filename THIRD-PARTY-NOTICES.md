# Hinweise zu Drittanbieter-Komponenten

Totally Easy Finanzplaner selbst steht unter der [MIT-Lizenz](LICENSE). Das Programm verwendet **keine
zusätzlichen NuGet-Pakete**. Es baut ausschließlich auf den folgenden Komponenten auf:

| Komponente | Verwendung | Lizenz |
|---|---|---|
| [.NET 10](https://github.com/dotnet/runtime) (Laufzeit und Basisbibliotheken, u. a. `System.Text.Json`, `System.Security.Cryptography`) | Laufzeitumgebung, JSON, Verschlüsselung | MIT, © .NET Foundation and Contributors |
| [Windows Presentation Foundation (WPF)](https://github.com/dotnet/wpf) inkl. Fluent-Theme (`PresentationFramework.Fluent`) | Benutzeroberfläche | MIT, © .NET Foundation and Contributors |
| Segoe UI Variable, Segoe Fluent Icons / Segoe MDL2 Assets | Schrift und Symbole | Schriftarten von Windows. Sie werden zur Laufzeit vom System geladen und **nicht** mit dem Programm verteilt. |

Die vollständigen Lizenztexte von .NET und WPF stehen in den jeweiligen Repositories:

- https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- https://github.com/dotnet/wpf/blob/main/LICENSE.TXT

Wird das Programm als *self-contained* veröffentlicht (`--self-contained`), enthält die erzeugte `.exe` die
.NET-Laufzeit. Dann gelten zusätzlich die Hinweise in
[THIRD-PARTY-NOTICES.TXT von dotnet/runtime](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).

Das Programmsymbol (`Assets/app.ico`, `docs/icon.png`) wird von `Assets/make_icon.ps1` erzeugt. Es ist Teil
dieses Projekts und fällt unter dieselbe MIT-Lizenz.
