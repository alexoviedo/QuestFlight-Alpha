# Boulder soaring demo candidate — 2026-10-17

Approved scope: full ground-launch sightseeing demonstration and short airborne tour, using the existing native SGS reference model, menus, instructor audio and authored wildlife. Implementation began 20:29:09 UTC; hard stop 22:29:09 UTC.

## Reference configuration and sources

Mile High Gliding publicly describes local sightseeing and discovery flights of different lengths. This inspired the pacing, quiet post-release glide, thermalling explanation, sightseeing and unpowered return. This candidate uses the SGS trainer; it does not reproduce an operator-approved route, aircraft configuration or commercial scenic-flight service. Public descriptions: https://www.milehighgliding.com/glider-flights

The City identifies light-wind Runway 8 preference, an inside glider pattern, 6,300 ft MSL pattern altitude and parallel grass glider runway. The existing native development launch/landing geometry remains on the main paved strip. Actual glider-strip staging, noise-area compliance and local operator procedures are NOT accepted by this change. https://bouldercolorado.gov/services/airport-traffic-pattern

Golden eagles can circle in thermal lift; the encounter is an authored fictional wildlife event, not a prediction of wildlife at a specific location. Its motion uses the same sampled atmospheric field, existing bird mesh and fixed world habitat; it is never scaled up or attached to the camera. https://academy.allaboutbirds.org/golden-eagle-soaring-on-a-thermal/

Landmark descriptions derive from the City and University public pages. Anchors are approximate local map features, not surveyed points: https://bouldercolorado.gov/locations/boulder-reservoir ; https://bouldercolorado.gov/trail/mount-sanitas ; https://bouldercolorado.gov/services/climbing-open-space ; https://www.colorado.edu/orientation/self-guided-campus-tour

## Implemented behavior

Home → Fly SGS sailplane → Scenic soaring demo → Full flight or Short tour. The same page offers captions/audio and Back. During flight, Menu offers pause, tour options/restart, safe control takeover, automatic return and finish. Replacement requires confirmation; cancel preserves the flight. Return and handover never relocate an airborne aircraft. Unsafe conditions pause with restart/end paths; no powered glider recovery is used.

The instructor uses ordinary control commands into native JSBSim. Full mode stages the real native pair, explicitly skips the interactive preflight without credit, clears the launch, flies the tow, releases, climbs in lift, follows sightseeing legs and lands to a complete stop. The tug returns independently. Short mode explicitly begins airborne. Landing uses the combined airbrake/wheel-brake command and the cockpit handle follows that command.

The demo weather is a repeatable reference, not observed Boulder weather: light easterly flow, existing bounded convective/radius/mixing-height ranges, long-lived thermals. No upward force, speed or position is injected to obtain the measured climb. A 150 m total-energy-height gain is required before sightseeing; low energy or expired lift causes an early return.

The existing 24-bird population, 16-visible/30,000-visible-triangle cap, SGS 8,000-triangle ceiling, render quality, scenery and native clock are retained. No new runtime hierarchy, physics authority, PC dependency, SpaceWarp or simulation-time dropping was added. New narrated audio cost is measured separately.

23 Kokoro-generated mono 24 kHz clips use the existing instructor audio source. Source/license and file hashes: `QuestFlight/Assets/Production/Training/Voice/SGS_DEMO_KOKORO_SOURCE_MANIFEST.json`. Script text: `tools/audio/soaring_demo_voice_scripts.json`. Human listening remains pending. Routine narration respects pause, single-utterance scheduling, captions and repeat; release/final calls have priority. Landmark facts require a visible projected anchor and unobstructed sampled terrain line of sight.

## Acceptance record

Live results and remaining gates are in SOARING_DEMO_STATUS.json. Evidence is external under `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/soaring_demo_20261017`.

Existing SGS stall/control-response fidelity remains unaccepted. This demo guidance work does not fix or certify that flight model. Existing failed Quest timing and 23 standard visual-QA failures are not waived. Editor scenario timing means simulated flight duration, never Quest CPU/GPU timing. Required Meta XR Simulator recordings were absent in the fresh audit; editor stereo previews must not be relabelled as simulator evidence.
