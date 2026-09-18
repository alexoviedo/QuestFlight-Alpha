# Touch thumbstick rudder

Right Touch thumbstick horizontal movement controls rudder in Free Flight: left is left rudder, right is right rudder. The left thumbstick and both vertical axes retain their existing roles. This allows left-hand yoke use with right-thumb rudder input.

The 12% center dead zone is rescaled over the remaining travel to reach full rudder. An off-center stick temporarily takes precedence over the external controller's rudder. Centering restores that source (neutral when no external input exists). It does not add the two sources together or select the Quest peripheral as a generic gamepad.

Mode selection, seat calibration, pause, deterministic demonstration, lesson guidance, unavailable right-controller orientation tracking and loss of app/input focus suppress this Touch channel. Neutral is required before rearming after suppression, avoiding held menu inputs becoming sudden rudder commands. Input suppression/disabling clears the latch. Existing native JSBSim receives the ordinary `AircraftControlState.rudder` channel; no flight model, camera, hierarchy, control-surface geometry or rendering pipeline was changed.

Implementation: `ProductControlInput`; the existing authored flight-mode coordinator is discovered once during Awake solely to read mode/calibration state. No hierarchy is constructed and no scene-wide searches occur per frame. Right Touch is read explicitly through the existing Meta input API.

Validation: three focused EditMode cases passed: neutral-before-engagement and left/right travel, focus/tracking loss and reacquisition, and proportional dead-zone/external-controller handback including invalid input. Evidence: `../T2-QuestFlight-product-artifacts/touch_rudder_20260918/tests.xml`. Android ARM64/Vulkan/OpenXR build succeeded, all 1,912 packaged scenery files were verified, and the exact APK was installed and launched on Quest 3. Installed SHA-256: `ae0e7ca7aca2c08d15c027cfaeeba77862ed542d8e344b4616d106ac0a64636d`. Build/install evidence is in the adjacent `android/` artifact directory. Worn input acceptance remains pending.

Headset check: choose Free Flight, center the right stick, then move it left/right while observing the rudder pedals or taxi steering. Release to center. Open seat calibration and confirm its stick use does not steer. This input-only change does not require another scenery/simulator capture campaign.
