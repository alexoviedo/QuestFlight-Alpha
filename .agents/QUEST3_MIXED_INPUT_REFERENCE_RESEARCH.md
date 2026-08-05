# Quest 3 mixed-input reference research

Date: 2026-08-04  
Repository baseline inspected: `checkpoint/pre-mixed-input-20260803` at `3349110b1e36df7624142444b2cd5aa5e53c41b4`  
Rejected implementation inspected: `rejected/mixed-input-v1-20260804` at `948284da6c4af14ac8b49cbab04c0c6bc88553a8`

## Scope

This is a research and architecture record only. No product code, package, project setting, prefab, scene, or asset was changed while producing it.

The required behavior is:

- each side can independently use a tracked hand or Touch Plus controller;
- hands and controllers can remain available concurrently, including mixed left/right combinations;
- users can transition without restarting the app or entering a global input mode;
- either modality can operate represented cockpit controls;
- Touch controllers can continue supplying flight axes while a hand operates the cockpit;
- control ownership is deterministic and cannot become stuck when tracking or application focus is lost;
- the existing OpenXR camera, seat calibration, aircraft authority, and cockpit state authority remain singular.

## Executive conclusion

The best Quest 3 reference is Meta's **current Multimodal + Interaction SDK Comprehensive Rig**, specifically the supplied `ConcurrentHandsControllersExamples` scene. It is closer to the product requirement than the ordinary hand/controller auto-switch patterns used by many XR examples. Meta's sample explicitly supports hands and controllers simultaneously and demonstrates using a controller with one interaction while a physical hand performs another; it also demonstrates poking with a hand that is holding a controller.

The rejected QuestFlight implementation did not fail because mixed input is unsupported. It failed because it replaced a proven input route with a large, custom interaction stack before proving the official stack on hardware. It duplicated SDK responsibilities for tracking activity, interactor lifecycle, hand visibility, target discovery, selection, and arbitration. The hardware evidence never showed a successful chain from official tracked device to official interactor selection to cockpit state change.

The standard for a future attempt should be:

1. Pick one interaction framework and one XR rig authority.
2. First run Meta's unmodified concurrent sample on Quest 3.
3. Retain SDK-owned data sources, active states, interactors, interactor groups, visuals, and pointer lifecycle.
4. Add only a thin pointer-event-to-cockpit-command adapter.
5. Keep the known-good product route enabled until one real cockpit control passes all hardware modalities.
6. Expand control by control, with positive device traces and immutable-APK headset gates.

## Research findings

### 1. Meta Concurrent Hands and Controllers is the closest working reference

Meta's current `ConcurrentHandsControllersExamples` scene is the direct reference for this product's requirement. Meta says it demonstrates controllers and hands simultaneously, permits a free hand to work while the other hand uses a controller, and permits a hand holding a controller to poke. It uses `OVRControllerInHandActiveState` to determine whether a controller is held and to gate data sources or individual interactors.

Important consequences:

- There should not be one global `Hands` versus `Controllers` mode.
- Availability and interaction are side-specific.
- The SDK's controller-in-hand state should gate official data sources/interactors; product code should not reconstruct that state from loosely related tracking flags.
- A transient controller-in-hand misclassification must not release a cockpit control immediately. Meta documents that the in-hand signal is imperfect and recommends designing around short false transitions.
- The sample must be imported and built from the exact pinned SDK version; copying an old hierarchy from memory is not equivalent.

Reference: [Meta ConcurrentHandsControllersExamples scene](https://developers.meta.com/horizon/documentation/unity/unity-isdk-concurrent-hands-controllers-examples-scene/)

### 2. Meta Multimodal is a tracking mode, not merely a UI setting

Meta's current Multimodal documentation states that the mode simultaneously tracks both hands and controllers and automatically distinguishes controllers that are in-hand or detached. It supersedes older auto-transition and double-tap transition mechanisms. The official setup imports the Meta Core SDK multimodal sample, uses explicit in-hand and detached anchors, assigns prefab `ShowState` values, and relies on specialized detached-controller profiles.

The official troubleshooting test is concrete: confirm that the detached-controller action path produces data. A hand visual appearing is not proof that Multimodal is functioning, and a build-time checkbox is not proof that controller bindings are live.

Reference: [Meta Multimodal setup and limitations](https://developers.meta.com/horizon/documentation/unity/unity-multimodal/)

### 3. The OpenXR contract requires explicit runtime state handling

`XR_META_simultaneous_hands_and_controllers` is the underlying runtime contract. Khronos specifies that the feature starts **paused** and the application must resume it. A successful configuration/import does not establish that the session is currently resumed.

The runtime can change the current interaction profile independently for `/user/hand/left` and `/user/hand/right` when a controller is picked up or put down. The application or SDK integration must react to `XR_TYPE_EVENT_DATA_INTERACTION_PROFILE_CHANGED` rather than assuming a fixed profile for the session. When `XR_META_detached_controllers` is also active and bindings have been suggested, an unheld controller may move to the `/user/detached_controller_meta` path.

The detached-controller extension is currently not ratified. That does not make Meta's supported Quest feature unusable, but it means the exact runtime, SDK, feature flags, bindings, and headset OS must be pinned and tested rather than treated as a portable OpenXR guarantee.

References:

- [Khronos `xrResumeSimultaneousHandsAndControllersTrackingMETA`](https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrResumeSimultaneousHandsAndControllersTrackingMETA.html)
- [Khronos `XR_META_simultaneous_hands_and_controllers`](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XR_META_simultaneous_hands_and_controllers.html)
- [Khronos `XR_META_detached_controllers`](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XR_META_detached_controllers.html)

### 4. The Comprehensive Interaction Rig owns the interaction lifecycle

Meta's Comprehensive Interaction Rig is organized as:

```text
Data Sources -> Interactions -> Visuals
```

The current rig supports hands, controllers, and controller-driven hands. Its active-state hierarchy already distinguishes `Hand`, `Controller`, `Hand and no controller`, `Controller and no hand`, and `Controller and Hand`. Interactors are placed under the matching active-state branch and coordinated by an `InteractorGroup` when priority is required.

The current rig deliberately reuses inputs and visuals instead of duplicating entire hierarchies per modality. Meta also warns that synthetic-hand stage order matters; poke and grab can both modify the same hand data stack. Adding an independent visibility or pose lifecycle outside that stack can therefore disagree with the state that actually drives interaction.

For QuestFlight's Quest-only target, the **OVR Comprehensive Rig is Meta's most explicit path to current Quest-specific Multimodal behavior**. The UnityXR Comprehensive Rig is the portable alternative and supports the standard interaction set, but the reviewed Meta Multimodal recipe is explicitly documented around `OVRManager`, `OVRCameraRig`, in-hand anchors, and detached anchors. The reviewed documentation does not provide an equally explicit detached-controller recipe for grafting OVR Multimodal state onto an existing Unity `XROrigin`.

That means a future product attempt must not combine half of each rig. It must do one of the following after isolated headset proof:

- make a controlled, explicitly authored migration to the official OVR Comprehensive architecture while preserving exactly one OpenXR-owned camera and seat calibration hierarchy; or
- prove the exact current UnityXR concurrent sample on Quest and use that complete prefab contract without introducing parallel OVR anchors or a second camera authority.

The choice is an architecture gate, not an implementation detail.

Reference: [Meta Comprehensive Interaction Rig](https://developers.meta.com/horizon/documentation/unity/unity-isdk-cameraless-rig/)

### 5. Official interactors and pointer events are the stable abstraction boundary

Meta's Interactors calculate candidates, hover, select, unselect, and cancel through a defined lifecycle. An `InteractorGroup` is the SDK mechanism for prioritizing poke, grab, and ray interactors. When a selection begins, the standard group keeps the selecting interactor active until the selection ends. This is the correct place to prevent a poke/ray competition from switching the active interaction mid-operation.

Pointer events then provide a modality-neutral contract:

- `Hover`
- `Unhover`
- `Select`
- `Unselect`
- `Move`
- `Cancel`

They include the source interactor identifier, interaction pose, and data. Meta explicitly describes this contract as allowing a shared handler such as `Grabbable` to consume Ray, Poke, and Grab interactions without coupling itself to their implementations.

QuestFlight's integration boundary should therefore be:

```text
OpenXR/Meta runtime
  -> one official per-side data source
  -> official active state and interactor group
  -> official Poke/Grab/Ray interactor
  -> official Interactable / PointerEvent
  -> thin semantic command adapter
  -> existing authoritative cockpit state
  -> existing visible control animation + feedback
```

Product code should not poll hand joints, infer pinches, raycast cockpit targets, and recreate the same lifecycle in parallel.

References:

- [Meta Interaction SDK interactors](https://developers.meta.com/horizon/documentation/unity/unity-isdk-interactor/)
- [Meta interactor/interactable lifecycle](https://developers.meta.com/horizon/documentation/unity/unity-isdk-interactor-interactable-lifecycle/)
- [Meta InteractorGroups](https://developers.meta.com/horizon/documentation/unity/unity-isdk-interactor-group/)
- [Meta Pointer Events](https://developers.meta.com/horizon/documentation/unity/unity-isdk-pointer-events/)

### 6. First Hand shows clean switching and state decoupling

Meta's First Hand is a shipped sample with public source. Its `CompoundHandRef` implements `IHand`, wraps tracked-hand and controller-hand references, selects the first connected source each frame, and delegates the complete `IHand` interface. Gameplay code consumes one stable interface rather than branching on devices. Its `InteractionTracker` observes ISDK selection state, and gameplay systems use composable `IActiveState` objects rather than maintaining parallel booleans.

This is a useful pattern for controls that should work with **either** source. It is not, by itself, the solution for using multiple sources on the same side concurrently because `CompoundHandRef` chooses one connected reference. Concurrent operation should use the current Multimodal/Concurrent sample and its active-state/interactor structure.

References:

- [Meta First Hand sample overview](https://developers.meta.com/horizon/documentation/unity/unity-sample-first-hand/)
- [Meta First Hand source](https://github.com/oculus-samples/Unity-FirstHand)

### 7. Spirit Sling shows the correct domain integration pattern

Meta's Spirit Sling is another public, runnable ISDK application. A grabbable object has modality-specific `HandGrabInteractable` and `GrabInteractable` components, while the application listens to the shared `Grabbable.WhenPointerEventRaised` stream. Its gameplay code reacts to `Select`, `Unselect`, and `Cancel`; it does not implement separate hand and controller target solvers.

For a cockpit control, the equivalent is one semantic control with official modality-specific interactables feeding one adapter and one authoritative control state. It is not one complete control implementation per device.

References:

- [Meta Spirit Sling ISDK integration](https://developers.meta.com/horizon/documentation/unity/spirit-sling/)
- [Meta Spirit Sling source](https://github.com/oculus-samples/Unity-SpiritSling)

### 8. Unity XRI is a valid alternative stack, but its default manager swaps modes

Unity's current Hands Interaction Demo is a working OpenXR/XR Hands reference. It includes a rig supporting hands and motion controllers, poke/direct/ray interactors, interaction groups, gesture detectors, stabilized attach poses, hand visuals, and project validation. It is a credible portable alternative to ISDK.

However, Unity's `XRInputModalityManager` explicitly toggles hand groups off when controllers are used and controller groups off when hand tracking is used. That default behavior implements runtime swapping, not Meta's full simultaneous Multimodal requirement. It should not be combined with ISDK interactors for the same cockpit targets, and it should not be assumed to solve detached-controller tracking.

If QuestFlight chooses XRI, it should choose XRI end-to-end and separately prove a supported Meta Multimodal bridge. Given the standalone Quest 3 product target and the exact concurrent requirement, Meta's current ISDK concurrent sample is the more direct standard.

References:

- [Unity XRI Hands Interaction Demo](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.0/manual/samples-hands-interaction-demo.html)
- [Unity XR Input Modality Manager](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.0/manual/xr-input-modality-manager.html)
- [Unity XRI Examples](https://github.com/Unity-Technologies/XR-Interaction-Toolkit-Examples)

## Comparison with the rejected QuestFlight implementation

The following are facts from the repository diff and prior headset evidence, not speculation.

| Working reference pattern | Rejected QuestFlight pattern | Consequence |
|---|---|---|
| Prove the official sample scene first | Integrated broadly into the product before an isolated sample passed | SDK configuration, rig wiring, cockpit binding, and authority bugs could not be separated |
| One SDK-owned tracking and active-state lifecycle | Added `MetaXrTrackingActivityAdapter` and independent tracking-loss grace logic | The adapter could disagree with the SDK interactor's real enabled/hover/select state |
| SDK-owned hand visual lifecycle and synthetic-hand stack | Added `MetaHandVisualStabilityController`, which toggled renderers from the custom adapter | Stable-looking hands were possible even when no usable interactor was driving them |
| Official Interactor computes candidates and selection | Added `MetaHandCockpitInteractionSource` that directly sampled index-tip/pointer poses, inferred pinches, resolved targets, and emitted its own interaction lifecycle | ISDK's candidate, selector, active-state, group priority, and pointer lifecycle were bypassed |
| Shared interactable pointer stream | Added multiple semantic source paths plus `MetaXrSemanticCommandAdapter` with explicit pointable/interactor filters | A valid SDK event could be discarded by an identifier/filter mismatch; no worn trace proved the event reached the adapter |
| SDK InteractorGroup owns poke/ray priority | Added a separate `MixedCockpitInteractionCoordinator` and modality priority/ownership model | Interaction priority existed in two layers and could diverge |
| Preserve known-good route until replacement proves parity | Authoring explicitly set both `LegacyTouchV2SemanticSource` and `CockpitTouchInteractionV2Controller` to `enabled = false` | When the new route failed, the previously working Touch cockpit route was already gone |
| One supported rig and tracking origin | Grafted Meta Core/ISDK/OVR behavior onto an existing Unity `XROrigin`, including OVR tracking-origin authoring | A FloorLevel/EyeLevel mismatch moved the pilot to the roof; fixing the pose did not fix input |
| Small vertical slice | Changed 152 files with 23,956 insertions and 350 deletions | Causal review and hardware diagnosis became impractical |
| Positive hardware event evidence | Structural validators counted components/references and startup configuration | Hands could appear and simultaneous enable could report success while zero usable cockpit selects occurred |
| Current OpenXR hand skeleton path | The rejected stack included Meta Core/Interaction/Interaction OVR 205 plus custom fallbacks to `OVRHand` | Multiple possible hand data/lifecycle paths existed even though ISDK has deprecated its legacy OVR hand skeleton in favor of OpenXR |

### Relevant rejected implementation size

- full rejected diff: 152 files, 23,956 insertions, 350 deletions;
- `MetaXrHybridRigAuthoring.cs`: 1,262 lines;
- `MixedInputProductIntegrationAuthoring.cs`: 1,200 lines;
- `MetaHandCockpitInteractionSource.cs`: 576 lines;
- `MetaControllerCockpitInteractionSource.cs`: 438 lines;
- `MetaXrSemanticCommandAdapter.cs`: 493 lines;
- `MetaXrTrackingActivityAdapter.cs`: 405 lines;
- `MixedCockpitInteractionCoordinator.cs`: 209 lines;
- `MetaHandVisualStabilityController.cs`: 90 lines;
- product aircraft prefab: 2,742 inserted lines in the rejected diff.

### Confirmed failure facts

- The known-working legacy Touch cockpit route was disabled by design before replacement parity was proven.
- The hand visuals eventually appeared steadily, but worn tests did not produce reliable cockpit interaction.
- Touch interaction did not work reliably; one interim build moved the yoke, but the full route never passed.
- There was no clean transition among hand and controller combinations.
- A tracking-origin mismatch caused a severe viewpoint regression.
- The official interactor hierarchy and simultaneous-mode call could exist without a proven `Select -> cockpit command -> visible state` chain.

### What remains unproven

It would be inaccurate to name one exact final API defect from the available evidence. The failed branch was rolled back before an in-headset trace recorded, for a worn/focused session, every SDK data source, active state, interactor candidate, pointer event, adapter acceptance, command, and authority mutation. Potential failures such as source filtering, active-state gating, target geometry, or profile binding are credible, but they were not individually isolated. A future attempt must produce that trace before attributing a root cause.

## Recommended QuestFlight standard

### Architecture decision

Use **one pinned Meta XR Core + Interaction SDK version and its supplied Concurrent Hands/Controllers sample as the acceptance oracle**. Do not revive the rejected custom tracking, visibility, hand-poke, controller-ray, or arbitration classes.

Because QuestFlight is standalone Quest 3 only, Meta's OVR Comprehensive Multimodal architecture is the leading choice. It has the clearest current documentation for in-hand state, detached paths, simultaneous tracking, and the matching sample. But it must enter the product as an intentional single-rig authority decision—not as an additional manager attached to the existing rig.

Before any product migration, compare two isolated APKs on the same headset:

1. the exact Meta `ConcurrentHandsControllersExamples` OVR sample;
2. the exact current Meta UnityXR Comprehensive sample, if its package contains a supported concurrent configuration.

Select the architecture that passes the required matrix and can preserve one OpenXR-owned camera, one XR origin, and the existing seat-relative calibration transform. Do not proceed if doing so would require two camera rigs or manual writes to the Main Camera.

### Product event boundary

The only new product-specific runtime layer should be a thin adapter that consumes an official interactable's pointer events and calls the existing semantic cockpit command authority. It may:

- identify the selected control;
- translate `Select`, `Move`, `Unselect`, and `Cancel` to semantic commands;
- attach a per-control owner token for the duration of a selection;
- release ownership on unselect, cancel, tracking loss, focus loss, or bounded timeout;
- trigger light controller haptics and visual/audio feedback.

It must not:

- infer device tracking availability;
- show/hide hand renderers;
- compute hand pinch or poke state;
- raycast to discover cockpit targets independently of the official interactor;
- choose a global modality;
- write the XR Origin, camera, or seat transform;
- write animated cockpit transforms outside the cockpit authority.

### Cockpit interaction mapping

Use the interaction type that matches the physical control:

| Cockpit control | Hand route | Touch route | Shared result |
|---|---|---|---|
| push button | official Poke | direct touch or short ray + trigger | momentary press/release |
| toggle switch | Poke or constrained HandGrab | direct grab or short ray + trigger | deterministic toggle/detent |
| rotary knob | constrained HandGrab/TouchGrab | constrained Grab, with stick fallback only if necessary | bounded rotary value |
| lever/throttle/mixture | constrained direct HandGrab | constrained direct Grab | continuous authoritative value |
| yoke | two-axis constrained grab if hand use remains required | controller flight axes remain primary; direct grab only if it does not conflict | flight authority input, not free transform motion |

Hands should not be expected to collide physically with the panel. Credibility comes from correct depth occlusion, a moving control surface, immediate state feedback, and bounded haptics/audio—not from unstable full-hand rigid-body physics.

### Input ownership and flight controls

- Ownership is per control, never global.
- Left and right sides are independent.
- Selecting one control must not disable the other hand/controller or unrelated flight axes.
- A Touch trigger may be suppressed from a conflicting flight binding only while that exact controller owns a cockpit selection.
- A brief false controller-in-hand transition must not instantly drop a held control; require a small, measured release grace period at the **ownership boundary**, not a second tracking lifecycle.
- On focus/tracking loss, synthesize one `Cancel`, release ownership, and restore flight-axis routing.

## Hardware-first acceptance process

Automated tests remain regression protection, but they cannot prove Quest interaction. The following gates prevent another false positive.

### Gate 0: exact-version compatibility

Record:

- Unity editor version;
- Meta Core and Interaction SDK versions;
- Unity OpenXR, XR Hands, and Input System versions;
- Quest OS/runtime version;
- enabled Android OpenXR feature/profile list;
- whether the exact rig is OVR or UnityXR;
- successful Multimodal resume result;
- active standard and detached interaction profile per side.

The current accepted baseline is Unity `6000.3.8f1`, Input System `1.18.0`, OpenXR `1.16.1`, XR Management `4.5.4`, and does not contain Meta SDK or XR Hands packages. The rejected branch added Meta Core/Interaction/Interaction OVR `205.0.0` and XR Hands `1.7.3`. Any future version set is a deliberate, pinned change.

### Gate 1: untouched official sample on Quest 3

Build the exact package sample without cockpit code. It must pass:

| Configuration | Required behavior |
|---|---|
| both hands | both independently poke/select |
| both Touch controllers | both independently direct/ray select |
| left hand + right controller | both remain active and interact |
| left controller + right hand | both remain active and interact |
| hand while same-side controller is held | behavior matches the official concurrent sample |
| controller put down and picked up | correct in-hand/detached state and profile transitions |
| repeated transitions | no restart, flicker loop, or stuck selection |

If this fails, only SDK/runtime/project configuration is investigated. The product scene stays untouched.

### Gate 2: one product control with the legacy route retained

Add one conspicuous cockpit control—preferably avionics master—without disabling the accepted Touch route. The official path is enabled only for that test control. Require a durable on-device trace:

```text
runtime profile
-> SDK data source connected/valid
-> active-state branch
-> interactor enabled
-> candidate control
-> Hover
-> Select
-> adapter accepted
-> semantic command
-> authority mutation
-> visible switch/state change
-> Unselect/Cancel and ownership release
```

No absence-of-error result counts as a pass.

### Gate 3: transition and recovery matrix

For the one control:

- 20 selections with each supported route;
- zero wrong-target selections;
- zero stuck selections;
- hand/controller transition under 500 ms after the runtime reports a valid source;
- controlled tracking loss during hover and select;
- application focus loss and regain;
- controller put-down/pick-up;
- flight axes remain live except for the exact selected control conflict.

### Gate 4: expand by interaction class

Expand buttons first, then toggles/detents, then rotary controls, then continuous levers, and finally any direct flight-control manipulation. Each class passes the complete hardware matrix before the next begins. Do not generate a broad cockpit binding catalog before the preceding class is proven.

### Gate 5: immutable full candidate

The same APK must pass:

- every represented interactive control with every supported modality;
- stable hand and controller visuals for 60 seconds;
- mixed left/right operation;
- no stuck ownership after tracking or focus loss;
- no controller-trigger/flight-axis conflict;
- no camera, seat, cockpit, aircraft, physics, or scenery regression;
- no crash, ANR, OOM, or Unity exception;
- acceptable physical Quest frame timing and thermal state.

## Stop conditions

Stop the next attempt immediately if any of these occur:

- the official sample has not passed on the connected Quest;
- a second XR Origin, Main Camera, HMD source, or seat calibration authority appears;
- product code starts deciding whether the SDK thinks a hand/controller is active;
- the known-good route is disabled before the one-control slice passes;
- the first slice grows beyond one control or one interaction class;
- a structural test is presented as proof of hardware interaction;
- the candidate lacks a positive end-to-end device trace;
- OVR and UnityXR rig components are mixed without an official sample demonstrating that exact composition.

## Source quality and limitations

Only primary sources were used: current Meta documentation, Meta-owned sample repositories, Khronos OpenXR specification pages, and Unity documentation/sample repositories. The web material was accessed on 2026-08-04.

This report establishes a best-practice architecture and proof process. It does not claim that a future integration will pass until the exact pinned sample and product candidate are tested on the physical Quest 3. In particular, it does not retroactively prove which single filter or binding rejected events in the failed branch; the earlier instrumentation was insufficient to isolate that point.

