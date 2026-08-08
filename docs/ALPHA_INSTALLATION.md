# QuestFlight private alpha installation

QuestFlight 0.1.0 Alpha 1 is a sideloaded standalone Meta Quest 3 build. It does
not require a PC after installation, but the initial install requires a USB
connection and a developer-enabled headset.

## Download

1. Sign in to the GitHub account that Alex has granted access to the private
   `alexoviedo/T2-QuestFlight` repository.
2. Open the repository's **Releases** page and select `v0.1.0-alpha.1`.
3. Download `QuestFlight-0.1.0-alpha.1-Quest3.apk` and
   `QuestFlight-0.1.0-alpha.1-SHA256.txt`.
4. Verify the checksum before installing.

## Install

Enable developer mode for the Quest 3, connect it by USB, accept the headset's
USB-debugging prompt, and install with Android Debug Bridge:

```powershell
adb devices -l
adb install -r .\QuestFlight-0.1.0-alpha.1-Quest3.apk
```

Exactly one authorized Quest should appear before installation. The APK package
identifier is `com.alexoviedo.t2.questflight`.

## Launch without the PC

1. Disconnect the USB cable after installation.
2. In the Quest application library, open the source/filter selector.
3. Select **Unknown Sources** (or the current Horizon OS equivalent for
   developer-installed applications).
4. Launch **QuestFlight**.

The application remains installed until it is uninstalled or replaced by a
newer APK. Guardian/boundary and controller prompts remain owned by Horizon OS.

## Tester expectations

- Use a Quest 3 while seated naturally.
- Complete Guardian and controller prompts before launching.
- The alpha is an early private build; report crashes, discomfort, repeated
  recovery cuts, interaction failures, or obvious visual regressions to Alex.
- Do not use it for navigation or claim flight-training credit.
