# QuestFlight private alpha installation

QuestFlight 0.1.0 Alpha 1 is a standalone Meta Quest 3 build. The preferred
tester route is Meta's private Alpha release channel: it requires no GitHub
account, PC, USB cable, SideQuest, ADB, or developer mode.

## Install from the Meta Alpha channel

1. Open Alex's QuestFlight Alpha invitation link while signed in to the Meta
   account used by the headset.
2. Select **Join**.
3. On the Quest 3, open **App Library** and select **Not Installed**.
4. Select **QuestFlight**, then **Install**.
5. Launch QuestFlight normally from the application library.

Meta limits private release-channel invitations to users who are at least 18.
The Alpha is not a public Meta Horizon Store release.

## Maintainer fallback: sideload the GitHub artifact

The private GitHub prerelease remains a maintainer fallback. It requires access
to `alexoviedo/T2-QuestFlight`, a developer-enabled headset, USB, and ADB.
Download `QuestFlight-0.1.0-alpha.1-Quest3.apk` and its checksum from release
`v0.1.0-alpha.1`, then verify the checksum before installing.

Enable developer mode for the Quest 3, connect it by USB, accept the headset's
USB-debugging prompt, and install with Android Debug Bridge:

```powershell
adb devices -l
adb install -r .\QuestFlight-0.1.0-alpha.1-Quest3.apk
```

Exactly one authorized Quest should appear before installation. The APK package
identifier is `com.alexoviedo.t2.questflight`.

## Launch a sideloaded build without the PC

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
