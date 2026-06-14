# Resolution Scaling Manual Checklist

Use this checklist before committing screenshot resolution changes. The app uses 3840x2160 as the canonical/base coordinate path; confirm that path still works after the scaling refactor.

## Screenshot Set

Recommended manual files:

- `Currency_1440p.png`
- `Currency_1080p.png`
- `Essence_1440p.png`
- `Essence_1080p.png`
- `Runes_1440p.png`
- `Runes_1080p.png`

Also smoke test one full 3840x2160 static stash screenshot when available to confirm the base path remains intact.

## Profiles

- 3840x2160: canonical/base path, scale 1.0.
- 2560x1440: scaled path, scale 0.6667.
- 1920x1080: scaled path, scale 0.5.

Each screenshot should be a full 16:9 uncropped static stash screenshot.

## Manual Checks

For Currency, Essence, and Runes:

- Load the screenshot with `File > Open Screenshot...`.
- Select the matching stash mode before loading.
- Confirm the status text shows the loaded filename and detected resolution.
- Confirm the stash crop is framed correctly.
- Confirm overlays sit on the visible stash slot boxes.
- Click several occupied and empty overlay boxes and confirm the correction dialog opens for the intended slot.
- For Essence, confirm static identity still maps each horizontal family left-to-right as Lesser, base, Greater, Perfect.
- Run AI Read Counts after loading and confirm the contact sheet/counts are built from the visible slot crops.

## Unsupported Resolution

Load or test a non-supported screenshot size and confirm the scan fails clearly. The message should include:

- the detected screenshot size,
- the supported sizes: 3840x2160, 2560x1440, 1920x1080,
- a note that the screenshot must be a full 16:9 uncropped static stash screenshot.
