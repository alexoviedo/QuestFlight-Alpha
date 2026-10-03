# Menu height and cockpit guide visibility

## Cause

`ProductUiPanelLayout` applied glare-shield clearance in every context. Long settings/help pages were raised even when the showroom had hidden the aircraft. The startup selection also used a fixed offset rather than the seated eye height.

The main-menu cockpit guide activated inspection highlights while `ProductFlightModeMenuView` still suppressed aircraft renderers. Earlier presentation QA exercised the guide from an active flight, missing the showroom entry path.

## Bounded correction

- Non-cockpit pages center 10 cm below a snapshot of seated eye height; the startup selection centers 12 cm below it. Both remain stable when looking around. No tracked-camera transform writes.
- Cockpit pages retain the minimum existing glare-shield clearance, avoiding the earlier instrument-panel intersection regression.
- Opening the cockpit guide or seat calibration from the showroom temporarily restores the existing aircraft/world presentation. Exiting restores the showroom and its renderer suppression. This does not start a flight, construct a cockpit or change flight authority.
- Added showroom → controls → cockpit guide → calibration → showroom coverage to editor and development-only stereo capture fixtures.

## Validation so far

Artifacts: sibling `T2-QuestFlight-product-artifacts/menu_context_20260929/`.

Compilation and editor QA completed. 66 interface captures, no capture errors; inspected centered main/settings pages and the visible cockpit with the airspeed reference. Standard QA generated 130 shots and retains the previous 42-material/38-target planning warning; it is not a clean machine-QA pass.

Stereo simulator rebuild and standalone Android build/install are pending disk space. No new APK has been installed. Cleanup of two generated desktop players was rejected by automatic approval review; user commands provided. Existing APKs, captures, source and personal files are preserved.
