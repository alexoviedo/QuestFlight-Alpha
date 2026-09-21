# QuestFlight interface polish plan

Status: approved by the user; implementation complete for the headset candidate. Focused editor visual review and Android build completed; final worn acceptance and comparative Quest GPU timing remain pending.

## 1. Recommendation and scope

Deliver one coordinated redesign of the existing shipping interface: startup, mode selection, lesson selection, briefing, orientation, live instruction, pause/help, debrief, seat calibration, loading and error states. The largest opportunity is typography, hierarchy, layout and consistent interaction feedback. These improvements do not require more scenery detail or a new rendering pipeline.

Art direction: modern aviation. Calm graphite surfaces, warm white typography, generous but purposeful spacing, restrained blue selection/guidance, and clear photographic context. Professional quality comes from coherent composition and predictable behavior. This is a polish target, not a claim of parity with MSFS or a guarantee of AAA quality.

Preserve the accepted September 21 ground markers, cockpit-reference anchoring and Medium fixed foveation. Preserve the existing lessons, evaluation, narration, saved progress, aircraft controls, universal Menu/Back behavior, fresh-flight resets and native JSBSim authority. No instructor avatar, new flight modes, new aircraft/scenery, extra weather work or controls redesign.

## 2. Evidence from this project

Inspected the existing September 19 screenshots under `training_clarity_20260919/standard_navigation_final/screenshots` and `standard_compact_panel/screenshots`, then compared their visible layout with current authoring/runtime source. These are existing editor captures, not a fresh headset audit; the subsequent user-accepted fixes are preserved and are not treated as unresolved defects. Capture artifacts in the scenery are outside this UI assessment.

| Observed problem | Current implementation evidence | Proposed response |
| --- | --- | --- |
| Startup title dominates; descriptive text and input help are very small. Large flat menu area competes with the aircraft backdrop. | `ProductStartupShowroomAuthoring`: individually positioned legacy TextMesh labels and box panels. | Compact branded header; readable action cards; deliberate space for the existing aircraft image. |
| Pause screen has a large slab, a large empty interval, then plain text choices. | `ProductAppNavigation.Refresh` uses fixed panel sizes and eight pre-positioned action slots regardless of visible count. | Content-sized panel; consistent header/body/actions/footer; clear filled selection state. |
| Selected actions are marked by a textual `>` and a small color change. | `ProductActionButton.Present`. | Stable selection surface, thin focus accent, explicit pressed state; text does not shift when selected. |
| Training cue combines ownership, stage, instruction and several controller hints in one text block. | `ProductTrainingController.RefreshUi`. | Separate a small status row, primary instruction and quiet contextual help row. |
| Cockpit labels are small plain text over detailed instruments. | Current `AuthorCoach` uses TextMesh at fixed instrument anchors. | Legible compact label with a restrained opaque backing; preserve corrected visual-root attachment and target alignment. |
| Product wording exposes internals; startup ready string has malformed separator encoding. | `ProductFlightModeMenuView`: native-flight initialization wording, `Â·`, plus an obsolete milestone fallback. | Short user-facing status; repair encoding; retain technical details in logs and meaningful recovery actions in errors. |
| Layout and styling are duplicated among startup, navigation, training and calibration. | Multiple TextMesh fields/material assignments and hand-authored coordinates. | One small shared style asset and reusable authored components, applied only to active shipping surfaces. |

The active lesson/navigation interface is `ProductAppNavigation`, not merely the legacy lesson text list still retained in the controller. Implement against the visible consumers and preserve compatibility where validators still reference legacy text.

## 3. Exact visual language

### Palette and surfaces

| Role | Color | Usage |
| --- | --- | --- |
| Canvas | `#101820` | Deep graphite menu background. |
| Panel | `#192630` | Main surfaces; solid behind reading areas. |
| Raised/selected surface | `#263B49` | Action focus and small inset groups. |
| Primary text | `#F2F4EF` | Warm white titles and instructions. |
| Secondary text | `#BBC8CF` | Supporting copy, readable without being dominant. |
| Accent | `#78C8E8` | Focus, primary actions, progress and guidance. |
| Caution | `#E6B66A` | Attention and caution with an icon and words. |
| Success | `#9FD2B5` | Completion with a check and words. |
| Urgent | `#F18B83` | Existing urgent instruction/error states with explicit text. |

These are proposed art-direction values. Check rendered contrast on the final surfaces; target at least 4.5:1 for ordinary reading text. Contrast alone does not establish VR legibility. Do not use color as the sole state indicator.

Use modest rounded corners, consistent padding and a thin border only where it clarifies focus. One softly shaded panel backing is enough. Reserve translucency for small decorative margins; readable text sits on a stable dark surface. Keep the accepted outdoor marker materials unchanged in this pass. Menu photography can show through the surrounding space rather than through paragraphs.

### Typography

Use **Inter Regular and Semibold**, with original font files and the SIL Open Font License recorded. One family, two weights, four roles: screen title, section/action title, body/instruction, secondary hint. Sentence case for actions and prose; uppercase only for a small wordmark or status tag. No thin fonts or italic instructional text.

Convert active product UI and teaching labels to TextMeshPro signed-distance-field text with static prebuilt font atlases and shared mobile unlit materials. TextMeshPro is already present in the installed uGUI 2.0.0 package; no new UI framework is needed. Use ordinary depth-tested text for cockpit references. Keep any existing overlay behavior deliberate and limited to modal UI; do not switch all text to always-on-top rendering.

Starting sizing targets, to confirm in headset: body/instruction cap height roughly 0.65–0.85 degrees, secondary essential text at least about 0.55 degrees, titles about 1.2–1.6 degrees. These are project starting targets, not certified thresholds. Define sizing by the seated pilot's angular view and actual font metrics, not desktop pixel counts. Avoid shrinking a whole panel until a long paragraph fits. Wrap by measured text width and expand/paginate paused explanatory copy if necessary, preserving its information and primary actions.

### Layout, input and motion

One spacing rhythm based on body line height; aligned left edges and consistent baselines. Bound each screen to a comfortable view, with the full primary action visible in both eyes. Set paused menu distance/scale together; do not blindly push cockpit annotations to menu distance or cover the panel and horizon with a giant interface.

Keep controller ray + trigger and stick + A, Menu, B/Y and X meanings intact. Give each current action an authored visual button; selected, hovered, pressed, unavailable and normal states share one design. Input hints use compact button glyphs plus words, including the Menu symbol distinguished from the system button. Build original glyph artwork into a small atlas; no paid UI pack is required.

Use short opacity/color easing, about 120–180 ms for focus and 180–240 ms for panel appearance. Activation is immediate and never waits for animation. No bouncing, rotating or floating instructional text; no scaling text on hover. Cockpit reference labels remain stationary relative to the interpolated cockpit root. Existing ground-marker motion remains at the accepted speed. Existing audio/haptics can be balanced and shared where already supported; no narration rewrite or new audio-content campaign.

## 4. Screen-by-screen result

| Surface | Intended presentation |
| --- | --- |
| Startup / main menu | Smaller wordmark and `Boulder Municipal · C172` context; three clearly designed mode cards using the current action order, each with a title and one readable sentence; seat setup as a secondary action. Keep and reframe the existing rights-cleared aircraft backdrop. Remove developer terminology from normal status. |
| Lesson list | Six clean rows with lesson number, readable title and accurate saved-completion state. No invented scores, estimated durations or locked curriculum. Selection is visibly highlighted without a shifting `>` prefix. |
| Briefing | Lesson title, compact description, existing cockpit-reference/practice/demo options as clear actions. One visually emphasized practice action, with the remaining choices still available. Preserve instructional substance. |
| Orientation / explanation | Compact panel sized to its actual content and actions; leave the referenced control visible. The instrument name, explanation and controller hint have distinct roles. |
| Live instruction | Small lesson/step header; one dominant current instruction; separate `Your controls` or `Instructor controls` status. X repeat and Menu help remain discoverable in a quiet footer. Urgent go-around instruction overrides ordinary emphasis. Split visual roles without rewriting lesson logic or suppressing corrections. |
| Cockpit reference labels | Same anchors and highlight geometry, sharper text and compact backing placed clear of the instrument face. A fixed fine leader links the label to its existing target. No bobbing or head chasing. |
| Pause and confirmations | Compact actions with shared hover/focus/pressed styling. A clear resume action; ending a session retains the existing confirmation and progress explanation. No empty eight-row slab on a three-action page. |
| Seat calibration | Same controls and save/cancel semantics; readable axis instructions and diagrams, small/large increment state, clear Save/Recenter/Reset actions. Human-readable offsets instead of a raw vector in the main reading block. |
| Debrief | Clearly separated outcome, existing feedback and Retry/Lessons/Main menu actions. Saved progress is represented accurately; no new scoring model. |
| Loading / error / unavailable | Consistent typography and surfaces; honest loading state with no fabricated percentage. Errors describe the user consequence and a valid action; diagnostic detail remains available in logs. Unsupported choices remain unavailable. |

## 5. Implementation sequence after approval

1. **Establish and apply the shared system.** Create font atlases, style tokens, panel/button primitives and controller glyphs. Produce three static compositions using real shipping content: main menu, paused lesson and in-flight instruction. These are a bounded layout step within the approved work, not a separate research milestone. Immediately implement the main menu and one training page to establish the reusable components.
2. **Apply that system to every existing menu state.** Replace the active text consumers, synchronize authored references and validators that depend on TextMesh, and retain existing navigation/actions. The conversion is scoped to presentation components, not the aircraft/lesson state machines. Remove malformed characters and obsolete product copy. Do not invoke broad training-authoring routines that regenerate lesson definitions, trim or voice recordings.
3. **Finish instruction and reference presentation.** Convert the live cue and cockpit labels, preserve their accepted anchors, finish calibration/debrief/errors, then apply consistent focus and transition treatment. Update text only when its content/state changes; avoid new per-frame string/layout work. Keep all UI explicitly authored and serialized.
4. **Package one coherent candidate for headset review.** Inspect representative left/right pilot views at actual scale and only the newly affected states, then build/install/hash-verify. Use existing capture tooling; no new simulator test campaign or repeated unrelated scenery/physics suites. Preserve the user's established fast headset workflow. One final worn session checks startup, a lesson, references, pause/back, calibration and debrief rather than asking for approval after each small style tweak.

The first visible deliverable must include both the redesigned main menu and training interface. A font-only patch, more marker effects, or a design document without the coordinated screen work is not completion.

## 6. Quest 3 limits and acceptance

Keep Android ARM64/Vulkan/OpenXR, Medium FFR, render scale and MSAA unchanged. Preserve one XR origin/camera and flight authority; never write the tracked camera. Keep references attached to `AircraftVisualRoot`, menus on the correct existing presentation anchors, and all hierarchy creation in editor authoring.

Use prebuilt SDF font atlases, one small icon/panel atlas and the existing background image. If a soft edge/shadow is needed, bake it into the panel atlas. Keep text live and sharp; do not flatten whole menus into images. Unlit surfaces, shared materials, inactive hidden panels and bounded transparent areas protect GPU cost. No bloom, screen-space blur, refraction, extra lighting, reflection cameras, moving background video or per-widget render textures.

Compositor layers can improve UI clarity according to Meta, but are deliberately not the first implementation: they add composition/occlusion/input and texture-update work. Consider a single panel layer only if actual Medium-FFR headset evidence still shows a text-quality limitation after the typography pass. Do not return to repeated FFR experiments as part of this plan.

Provisional engineering targets, not measured claims: no work from hidden menu renderers, no new persistent update loop for static labels, no steady per-frame allocations from the new presentation code, incremental UI asset residency within 16 MiB, and a target of no more than 0.3 ms additional p95 GPU time for the active training presentation versus the accepted Medium-FFR build in comparable conditions. Measure the final candidate on Quest when available; noisy or incomplete timing cannot establish a pass. If the added interface cost is material, simplify decoration and material passes before sacrificing text size or altering the scenery budget.

Acceptance is visible and behavioral: consistent typography/padding/states across the listed shipping screens; essential text readable without leaning at normal seated posture; all actions readable and targetable; no overlap/clipping/missing glyphs in either eye; no shaking, corner flips or focus-induced text movement; stable cockpit labels before and after touching controls; unchanged Menu/Back, repeat, resume, progress and fresh-flight behavior. Accept only after the user's final visual check. MSFS-like polish is the ambition; standalone performance and visual quality still require evidence.

## 7. Research informing the proposal

- [Meta typography guidance](https://developers.meta.com/horizon/design/styles_typography/): legible fonts, a limited consistent hierarchy, Inter and hardware readability checks. Its panel pixel sizes are not treated as world-space sizing guarantees.
- [Meta panel sizing](https://developers.meta.com/horizon/essentials/horizon-os-panel-sizing/): reading distance, comfortable view extent and pointer target sizing. OS-panel guidance informs ergonomics; this project remains an immersive controller-based flight simulator.
- [Inter project and license](https://rsms.me/inter/): source font, readable text design and SIL OFL 1.1. Preserve the actual license with any later imported font asset.
- [MSFS 2024 accessibility](https://www.flightsimulator.com/accessibility-msfs-2024/): treats text size, menu opacity and subtitle contrast as core presentation concerns. Borrow that attention to readability, not its visual identity, breadth or desktop interface density.
- [Meta panel implementation guidance](https://developers.meta.com/horizon/design/panels_implementation/): compositor layers as an available clarity technique, deferred here unless needed.
- Installed Unity documentation: `Library/PackageCache/com.unity.ugui@bb329a87fcdc/Documentation~/TextMeshPro/ShadersDistanceFieldMobile.md` confirms available mobile unlit SDF and distinct depth-tested/overlay options. Implementation must match the installed package and stereo shipping path.

All specific palette, layout, motion and cost targets above are recommendations for this project, not claims that the cited sources prescribe this exact design.


## 8. September 21 implementation record

Implemented a serialized presentation layer across startup, navigation, lessons, briefing, references, instruction, calibration and existing feedback states. Inter Regular/Semibold static SDF atlases replace visible legacy text while retaining the serialized TextMesh data contracts. ProductUiTheme supplies shared colors; ProductUiPanelLayout sizes panels from real content; ProductUiSurface provides 150 ms color feedback without moving text. Instruction separates ownership/progress, step, current cue and help. Cockpit labels keep the accepted AircraftVisualRoot anchor and normal depth testing.

Menus and instruction cards use dedicated overlay font/surface materials so cockpit windshield depth cannot hide their text. Calibration suppresses its legacy panel immediately when navigation owns its presentation. All scene hierarchy and assets are editor-authored by ProductInterfacePolishAuthoring; this does not invoke full lesson authoring. Existing lesson evaluation, narration, controls, Medium FFR and markers are preserved.

Implementation choices: use lightweight unlit procedural rounded surfaces instead of a panel texture atlas; compact typographic controller hints instead of a separate controller-icon atlas; explicit axis labels and centimeter offsets instead of a controller illustration. Existing transition timing is retained, with new motion limited to focus-color easing. No new postprocessing, lighting, render textures or UI framework. These choices keep this pass focused on hierarchy and readability.

Evidence is stored outside the repository at `T2-QuestFlight-product-artifacts/interface_polish_20260921`. The focused preview uses editor eye-offset cameras and explicitly staged UI states; it is not a player-loop, OpenXR stereo, lesson-progression or Quest performance test. Old global scenery audit failures remain recorded rather than waived. Final headset review must check text size, pointer targets, both-eye clarity, navigation and instruction while seated. The proposed 0.3 ms GPU and 16 MiB residency limits remain unmeasured targets, not achieved performance claims.

Android candidate built successfully; scenery inventory verified 1,912 files. APK SHA-256: `8641a57ad618c4cf9eaf7ea2945d1be6533a0de3afe328d4b747e41cc71b2f2a`. Focused inspection record: `interface_polish_20260921/interface_review.json`. Existing editor windshield/cockpit artifacts limit evaluation of depth-tested reference labels; final headset acceptance remains required.

## Accepted headset result

The user accepted the interface redesign ("This looks great") and then the bounded thumbstick-navigation follow-up ("That worked great"). All menus use either thumbstick up/down, A to select, B/Y to go back and the existing Menu function. Menu pointing is disabled; cockpit interaction is unchanged. Calibration uses held grip for seat adjustment and X for fine/coarse increments. Accepted installed APK SHA-256: `f41f33b86221a8528e9a7b8492c9f14a8713169078ae13633dd3c2934dae68a1`; build validation and all 1,912 scenery payload files passed. No automated suite or simulator rerun was requested for the input-only follow-up. Comparative GPU/thermal timing remains unmeasured.
