# Exile Ledger

Exile Ledger is a Windows desktop stash valuation and price-checking tool for Path of Exile 2.

It scans supported stash tabs from screenshots, reads stack counts, maps items to price data, and totals stash value. It is currently an early friends-alpha project focused on stash valuation, runes, currency, and runeshaping reward helpers.

## Current Status

Exile Ledger is in early development.

Expect bugs, imperfect item detection, and stash modes that still need manual correction. This build is intended for private testing and feedback, not public release.

## Features

* Scan supported Path of Exile 2 stash tabs
* Read stack counts from stash screenshots
* Map stash slots to known items
* Fetch price data from poe.ninja
* Estimate total stash value
* Highlight priced, unpriced, and unknown occupied slots
* Manually correct item names and stack counts
* Save per-tab mappings and overrides
* Refresh local poe.ninja icon data for item suggestions
* Optional AI-assisted layout/count reading for development and testing
* Runeshaping reward scanner with value comparison overlay

## Supported Workflows

### Stash Valuation

Choose a stash mode, open the matching stash tab in Path of Exile 2, then click `Scan Stash`.

Supported stash modes may include:

* Currency
* Runes
* Kalguuran Runes
* Essence
* Additional experimental stash layouts as they are added

Some stash modes require the in-game stash folder view. This setting is saved per stash mode.

### Manual Correction

After a scan, click highlighted stash slots to correct item names or counts.

Corrections are saved locally so future scans can improve over time.

### Runeshaping Helper

Click `Runeshaping` or press `F8` while the Runeshape Combinations panel is open.

The app captures the reward area, reads the reward rows, checks poe.ninja prices, and displays a value comparison overlay.

## Friends Alpha Download

For friends-alpha builds:

1. Download the release zip.
2. Extract the zip first.
3. Open the extracted `Exile Ledger` folder.
4. Run `ExileLedger.exe`.

Do not run the app directly from inside the zip.

If Windows SmartScreen appears, it is because this early build is not code-signed yet.

## Settings

Open `Settings` inside the app to configure optional features such as AI-assisted count reading.

If using OpenAI-based features, use your own API key. Normal stash scanning does not require an OpenAI API key unless an AI feature is enabled.

## Local Data

Exile Ledger stores user settings, mappings, corrections, cached icon data, and debug/helper files locally on your computer.

These files are not included in release zips.

## Development

Build locally with:

```powershell
dotnet build
dotnet run
```

Create a Windows x64 friends-alpha release zip with:

```powershell
.\tools\package-release.ps1
```

The packaging script publishes the app in self-contained single-file mode, stages only the intended release contents, wraps them in a top-level `Exile Ledger` folder, writes the zip to `artifacts\`, and prints the SHA256.

Use the script for GitHub release zips. Do not upload the old loose self-contained publish output, which includes hundreds of .NET runtime files and culture folders directly in the release root.

The underlying publish command is:

```powershell
dotnet publish ".\ExileLedger.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o ".\publish\ExileLedger-singlefile"
```

Expected friends-alpha publish layout:

```text
ExileLedger.exe
Tesseract.dll
assets\
Data\
x64\
README.txt
```

Release zips should wrap those files in a top-level folder:

```text
Exile Ledger\
  ExileLedger.exe
  README.txt
  Tesseract.dll
  assets\
  Data\
  x64\
```

## Notes

Exile Ledger is an unofficial fan-made utility and is not affiliated with or endorsed by Grinding Gear Games.

Path of Exile and Path of Exile 2 are trademarks of Grinding Gear Games.
