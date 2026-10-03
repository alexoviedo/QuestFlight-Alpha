# QuestFlight tester guide

This candidate is for standalone Quest 3 with Touch controllers. It runs offline. It is a simulator for feedback, not certified flight instruction. Your first session need not cover every feature.

## Start here

Sit comfortably with room to move both controllers. Complete the seat and control guide. Use the thumbstick to select menu items, **A** to choose, **B or Y** to go back, and the left controller's **Menu** button to pause/open help. The system button keeps its normal Quest function.

- **Free flight:** a fresh airport start under your control.
- **Flight lessons:** guided practice; begin with Cockpit and taxi.
- **Scenic demo:** an automated local tour and landing, with optional takeover from Menu.
- **Short showcase:** Flight tools → Short activities → Short showcase. Starts already airborne on the airport arrival and ends after landing and runway exit. Time varies with wind/traffic; it is not a shortened takeoff demonstration.

## Make it comfortable

Menu → Controls offers presets, rudder response/deadzone, yoke travel, trim speed and live input preview. Assignments offers a controller diagram and optional **press-to-grab yoke**. In that mode press again to release; pausing or losing tracking releases it. Default is hold-to-grab. Steering and trim sticks swap together; other cockpit grabs keep their normal behavior.

Readability settings separate menu text from narration text. The narration page offers larger captions and a solid or subtly transparent background. Voice, aircraft audio and haptics have separate controls. Full rudder movement stays available with every response preset.

## While flying

The title states your current objective; short subtitle phrases follow Michael's recorded speech. **X** repeats instruction/narration where offered. Pause → Help contains the full instruction. Cockpit guide explains and highlights instruments. The kneeboard includes an optional observed-state readiness checklist; outside traffic scans and other pilot checks are your responsibility and are never automatically checked off.

The map is schematic local orientation, not a navigation chart. Cyan shows the applicable route/target; gold is a bounded **recent** path. Demo takeover and return use the pause menu. If the demo cannot safely rejoin, keep control or restart; it does not teleport silently.

End a free flight through Flight tools → Finish flight. The review gives observed behavior and a direct practice recommendation. It is not a universal landing score. Guest lesson/history results are separate from the owner's.

## Report useful feedback

Pause → Flight tools → Mark an issue. Choose a category, then note the report ID shown. Tell the developer:

1. The report ID/build and activity.
2. What you expected and what happened instead.
3. Where you were looking or what control you used, and whether it repeats.

Reports stay on the headset. Nothing is uploaded. The developer can export them over USB using `scripts/export_headset_feedback.ps1`. A normal Quest screenshot/video can help explain a visual issue; reporting does not secretly record microphone or video.

## Current test boundaries

Touch controller and Quest comfort, caption readability, speech mix and sustained frame/thermal behavior still require this candidate's hardware acceptance. Desktop captures do not establish Quest performance. A shorter showcase has native scenario validation recorded with the build, but users may still find unexpected wind/traffic interactions. Arbitrary HOTAS mapping, multiplayer and new aircraft are outside this candidate.

If an activity cannot start, return to the main menu and try again. Restart the application if needed and report the build. If storage is full, history save errors are visible; free storage and use Retry saving. Corrupt history can fall back to the previous saved copy, which may omit the latest flight.
