<p align="center">
  <img src="assets/logo.png" width="128" alt="Game Manager logo">
</p>

<h1 align="center">Game Manager</h1>

<p align="center">
  See every game installed on your PC, grouped by drive — and uninstall the ones you don’t play anymore in one click.
</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Download GameManagerInstall.exe</b></a>
</p>

---

## Features

- **All your launchers in one list** — Steam, Epic Games, Xbox / Game Pass, GOG, Ubisoft Connect, EA app, Battle.net,
  Rockstar, Riot, HoYoPlay, GRYPHLINK, Heroic, itch.io, Meta Quest, Wargaming and more.
- **Filter by drive** — each drive card shows how many games it holds, their total size, and a games / other data / free space bar.
- **Filter by platform**, accent-insensitive **search**, and sorting by **size**, **name**, **platform** or **last played**
  (Steam), to spot the games you forgot about.
- **One-click uninstall** with a confirmation dialog. The official uninstaller of each launcher is used; the entry disappears
  automatically once the game is really gone, and a notification shows how much space was freed.
- **English and French** interface, **light and dark** themes (follows Windows by default).
- Runs **without administrator rights**; each uninstaller asks for elevation itself when it needs it.

## Installation

1. Download **GameManagerInstall.exe** from the [latest release](../../releases/latest).
2. Run it. Windows SmartScreen may warn you because the installer isn’t code-signed: click **More info → Run anyway**.

Requirements: Windows 10 (1809) or later, x64, and the [.NET Desktop Runtime 8+](https://dotnet.microsoft.com/download/dotnet/8.0)
— the installer detects it and offers to download it from Microsoft if it’s missing.

The app installs per user in `%LOCALAPPDATA%\Programs\GestionnaireJeux` and can be removed from **Settings → Apps**.

## Supported launchers

| Launcher | How games are found | How they’re uninstalled |
|---|---|---|
| Steam (all libraries) | `libraryfolders.vdf` + `appmanifest_*.acf` | `steam://uninstall/<id>` (Steam asks for confirmation) |
| Epic Games | launcher manifests (`*.item`) | game files deleted + game removed from the launcher (or “Open Epic Games”) |
| Xbox / Game Pass / Microsoft Store | packages with `MicrosoftGame.config` (`XboxGames` junctions resolved) | `Remove-AppxPackage`, like the Xbox app |
| GOG | `GOG.com\Games` registry key | `unins000.exe` |
| Ubisoft Connect | `Ubisoft\Launcher\Installs` registry key | `uplay://uninstall/<id>` |
| EA app, Battle.net, Rockstar, Riot, Amazon, Wargaming | Windows “Installed apps” entries | official uninstaller |
| HoYoPlay (Genshin Impact, Honkai: Star Rail, ZZZ…) | `Cognosphere\HYP` registry key | `launcher.exe --uninstall_game=…` |
| GRYPHLINK (Arknights: Endfield…) | launcher and game logs (its config is encrypted), folder verified | opens GRYPHLINK |
| Heroic (Epic / GOG / Amazon) | `%APPDATA%\heroic\…\installed.json` | opens Heroic |
| itch.io | `.itch\receipt.json.gz` receipts | opens itch |
| Meta Quest / Oculus | Oculus libraries (`Manifests\*.json`) | opens the Meta Quest Link app |
| Others (osu!, FiveM, Roblox, Nexon, Plarium, Gameforge, Garena…) | known games and publishers | official uninstaller |

GRYPHLINK, Heroic, itch.io and Meta don’t offer an uninstall command: Game Manager opens the launcher and the entry
disappears as soon as you uninstall the game there.

## Safety

- Game Manager never deletes files itself, except for **Epic Games** titles (the Epic launcher has no uninstall command).
  For those it refuses drive roots, system folders and library folders, never follows junctions or symbolic links,
  and backs up Epic’s manifests to `%LOCALAPPDATA%\GestionnaireJeux\Sauvegardes` before changing them.
- Every uninstall asks for confirmation first.
- Log file: `%LOCALAPPDATA%\GestionnaireJeux\journal.txt`.

## Building from source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
.\build.ps1 -Version 1.0.0
```

This publishes the app to `dist\` and builds `installer\Output\GameManagerInstall.exe`.

Run the tests (parsers, safety guards, Epic removal and launcher detection on fake installs — no real data is touched):

```powershell
dotnet run --project tests\SelfTest -- en
```

`GestionnaireJeux.exe --scan-json result.json` exports what the scan finds, without opening the window.

## Project layout

| Path | Contents |
|---|---|
| `GestionnaireJeux/` | WPF app (.NET 8): scanners, uninstall logic, UI, translations, themes |
| `installer/` | Inno Setup script |
| `tests/SelfTest/` | self-tests |
| `assets/` | logo and the script that generates it |
