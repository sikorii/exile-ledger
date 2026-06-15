# Exile Ledger (POE2 Price Checker App)

First-pass PoE2 Runes of Aldur runeshaping helper.

## Current Behavior

- Click `Runeshaping` or press `F8` for the encounter scanner.
- Choose a stash mode from the dropdown, then click `Scan Stash`.
- Check `In-game stash folder mode` for stash modes that live inside a Path of Exile stash folder. This setting is saved per stash mode and does not choose a Windows folder.
- Click `Icons` to refresh the local poe.ninja icon cache used by slot suggestions.
- Click `Copy Summary` to copy the current scan summary to the clipboard.
- Before live scans and captures, the app tries to bring the Path of Exile 2 window to the foreground.
- Press `F8` while the Runeshape Combinations panel is open.
- The app screenshots the selected 4K PoE2 screen and crops the runeshaping reward area.
- OCR parses all reward rows it can find.
- Prices are fetched from poe.ninja for the Runes of Aldur league.
- A topmost click-through overlay lists the choices:
  - green: best value choice,
  - yellow: decent value compared with the best,
  - red: low value compared with the best.
- Press `F7` while the currency stash tab is open for the first-pass currency scan.
  - Currency results appear in the app window, not as an overlay.
  - The app shows the captured stash-tab image with clickable slot boxes.
  - Click a boxed slot to enter/correct the item name.
  - The slot dialog shows poe.ninja icon suggestions when the local icon cache can match the slot art.
  - App-drawn slot counts appear in the bottom-left of each boxed slot so they do not overlap PoE's own stack count.
  - Your edits are saved in `config/currency-mappings.json`.
  - Unknown occupied slots are counted so the map can be expanded safely.
- Use `Augment: Runes` while the Aug(rune) stash tab is open on the Runes sub-tab.
  - Rune results appear in the app window.
  - Click a boxed rune slot to enter/correct names such as `Lesser Desert Rune`, `Desert Rune`, or `Greater Desert Rune`.
  - The slot dialog shows rune icon suggestions when the local icon cache can match the slot art.
  - Rune edits are saved in `config/rune-mappings.json`.
  - Count overrides are saved in `config/rune-count-overrides.json`.
  - Upgrade suggestions compare `1 higher rune` against `3 lower runes` and highlight profitable upgrades.
  - Eligible upgrade chains stop at Greater runes; Perfect and purple/drop-only runes are priced but not suggested as upgrade outputs.
- Use `Augment: Kalguuran Runes` while the Aug(rune) stash tab is open on the Kalguuran Runes sub-tab.
  - Kalguuran rune results appear in the app window.
  - Click a boxed Kalguuran rune slot to name or correct it using poe.ninja icon suggestions.
  - Edits are saved in `config/kalguuran-rune-mappings.json`.
  - Count overrides are saved in `config/kalguuran-rune-count-overrides.json`.
  - This tab prices mapped runes only; it does not run upgrade math.
- Use `Capture Stash Tab` with any stash tab open to save reference screenshots for building the next tab scanner.
  - Captures are saved in `debug/stash-tab-captures/`.
  - The app saves both timestamped files and `latest-stash-tab-*.png` convenience copies.
- Use `AI Layout` only when building a new stash scanner.
  - It sends the selected stash crop to OpenAI using the `OPENAI_API_KEY` environment variable.
  - The default model is `gpt-5.4-mini`; override it with `OPENAI_STASH_MODEL` if needed.
  - It is not used by normal `F7` stash scans.
  - Debug files are saved in `debug/ai-stash-analysis/`, including the request image, raw response, and parsed JSON output.
- Use the poe.ninja icon cache commands as a layout-building helper, not as mandatory scan logic.
  - `--icon-cache` downloads poe.ninja item icons into `cache/icons/` and writes `config/poe-ninja-icons.json`.
  - `--icon-match <screenshot> <currency|runes> [slotIndex]` writes ranked icon guesses to `debug/icon-match.txt`.
  - Items that share the same icon art can tie, so use the result as a mapping suggestion only.

The first pass is tuned from the provided `3840x2160` screenshots. Debug crops are written beside the built app in `debug/`.

## Development

```powershell
dotnet build
dotnet run
dotnet run -- --icon-cache
dotnet run -- --icon-match "C:/POE2 Price Checker App/publish/debug/currency-fullscreen.png" currency 23
dotnet run -- --kalguuran-runes-test "C:/POE2 Price Checker App/publish/debug/stash-tab-captures/latest-stash-tab-fullscreen.png"
```

The `Test Screenshot` button scans:

```text
C:\Users\maran\OneDrive\Desktop\runeshaping\Screenshot 2026-06-09 092011.png
```
