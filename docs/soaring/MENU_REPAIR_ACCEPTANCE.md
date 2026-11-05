# Menu repair acceptance - 2026-10-14

All approved fixes M01-M17 are implemented. Final candidate **0.1.0-alpha.3 / versionCode3** was built, verified and installed on Quest3 serial `2G0YC5ZG8907TD` without clearing app data.

[Exact APK](C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/menu_repair_20261014/final_android/QuestFlight-menu-repair-alpha3.apk) - 1,816,543,336 bytes.
SHA-256: `3ab4e5065c7399cf21762059d4e43cfba305ebbb98f5da5515efddb37236df86`. Installed base.apk matches exactly.

[Exact source/model/APK identity](C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/menu_repair_20261014/FINAL_ARTIFACT_IDENTITY.json) binds the archived source,297 dirty/untracked records,3,625 ignored authored input hashes, native/model bytes, donor-derived presentation and verification receipts. Native/model payload identity remains `0af74469d7c77b6d487d097fef4847633a649e588a9da3fee6d5d79902005ca7`. Archived inputs match their ledger and runtime inputs match the build snapshot. Original downloads, prior APKs and rollback assets remain preserved; no commit/merge/push.

| Acceptance | Status and evidence |
|---|---|
| Approved implementation | M01-M17 complete; details in [MENU_REPAIR_PROGRESS.md](MENU_REPAIR_PROGRESS.md). Home directly exposes Fly SGS; physical launch and lesson routes use the selected aircraft. |
| Software | Menu/flight-mode suite61/61; final shader/scope50/50; final SGS label/scope2/2; affected regression118/118; PlayMode31/31. Suites overlap and are not summed. Ten real player-loop flows include both tow runway transitions, all7 SGS and6 C172 lesson routes, SDK pointer events, Back/focus, confirmations, settings, calibration, history and already-paused lesson Resume. |
| Integrity/closure | PASS:5,179 inspected assets; closure753 approved,0 rejected,0 unknown. Matching stereo/motion-depth shader coverage PASS; diff whitespace check PASS. |
| Android/package/install | PASS: release ARM64, Vulkan/OpenXR, Quest3-only manifest, targetSdk34, profiling/debugging off; native ELF and ZIP16KiB alignment/signature pass; exact native libraries and all18 model payload files match; all1,912 scenery payload files match. Installed version and APK hash confirmed. |
| Saved progress | Retained byte-for-byte across upgrade: `70f9d055ae54ebc9303e63b3412b809b450b1f56a80dcafda47aeded195ef040`. |
| Menu visual evidence | Offline captures112 main+168 route/lesson+16 final-label images at100/120% text; inspected representative stereo pairs including corrected pause labels and SGS bindings. Live simulator45 images per aircraft/90 total; Home and SGS setup stereo inspected. No worn comfort claim. |
| Standard/static resources | FAIL retained baseline:107/130 standard views pass,23 fail. All129 non-selector draw/triangle/material estimates exactly match prior candidate. Home selector30 draws/99,488 triangles/10 materials. Standard maxima80 draws/967,513 triangles/37 visible materials exceed unchanged77/692,871/38 caps. Static gate rejects failed QA. No cap increases, scenery/quality reductions or simulation-time changes. |
| Simulator full gate | FAIL: mountain-identity check during multi-page showroom/world visibility sequence;13 other checks true, no reported errors or missing components. Live XR/stereo works; formal replay gate is not claimed passed. Final SGS yoke-to-stick wording was corrected after these captures and verified in offline stereo/software. |
| Quest timing/memory/thermals | UNMEASURED for this candidate. Device reports Asleep/proximityfalse. Normal launch request and process observation are startup evidence only; native model activation, focused rendering, per-frame CPU/GPU/p95, resident memory and sustained thermals are not accepted. |
| Aircraft fidelity/operator exactness | Previous reference limitations remain: SGS solo glide tolerance, moderate-ridge support and older C172 lesson probes are not repaired by a menu change. See [ACCEPTANCE.md](ACCEPTANCE.md) historical evidence and [DEFERRED_INPUTS.md](DEFERRED_INPUTS.md). No invented operator procedures or certification claim. |

No approved menu implementation is left pending. Remaining acceptance depends on a genuinely worn/focused headset; existing scenery/fidelity failures remain separate work.

When you return:

1. Open QuestFlight and confirm **0.1.0-alpha.3** on Home; complete normal Guardian/controller prompts and seat setup.
2. Choose **Fly SGS sailplane -> Ground aerotow -> Runway08 or26**. Verify cockpit arrival, control/release checks, explicit crew preparation and clearance.
3. Try point+trigger and stick+A; verify Back, Settings, Menu pause/Resume, confirmation Cancel, and the second lesson page. Return Home and select C172.
4. Fly one tow/release and unpowered landing; record menu readability/control issues. Physical timing/thermal acceptance still needs a dedicated genuinely focused session.

External evidence root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/menu_repair_20261014`. This report supersedes the alpha2 installed-candidate/menu status in earlier soaring notes.
