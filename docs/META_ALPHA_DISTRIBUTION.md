# QuestFlight Meta Alpha distribution

QuestFlight uses a private Meta release channel as its preferred friend-testing
route. A release-channel URL adds the build to a tester's normal Quest library;
it avoids GitHub authentication and sideloading tools. This is private Alpha
distribution, not public-store submission or release acceptance.

## Immutable candidate

- Source checkpoint: `30d09406e0f5f9eab77c00a17d7220cdb7aa1cf3`
- Version: `0.1.0-alpha.1` (`versionCode` 1)
- Package: `com.alexoviedo.t2.questflight`
- APK: `QuestFlight-0.1.0-alpha.1-Meta-Quest3.apk`
- Bytes: `174,634,793`
- SHA-256: `f3e5c2178f64f02b98424f5c4a61b13dcc728cab61c9b4cb82eb2425a35393f5`
- Certificate SHA-256: `2a505f05bdfa94a3934689140efd1fe907d5aed611fad5962b3b8b9b46709a21`
- Signing verification: Android APK Signature Scheme v2, one RSA-4096 signer
- External evidence directory:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\meta_alpha_distribution_20260807_205631`

Do not rebuild after upload unless the Android version code is incremented. Do
not replace or regenerate the signing identity.

## Private signing custody

The keystore and signing JSON live outside Git under
`%USERPROFILE%\.questflight\signing`. Windows ACLs restrict that directory to
Alex and the local SYSTEM account. `RECOVERY_INSTRUCTIONS.txt` explains the
required backup. Never commit the directory or copy its credentials into build
logs, tickets, chat, or documentation.

Build a Meta candidate with:

```powershell
.\scripts\build_meta_release.ps1
```

The wrapper requires the external signing JSON, fails closed when any signing
value is missing, and restores the exact serialized Unity project settings
after the build. Unity build evidence records `customSigning: true` without
recording credentials.

## One-time Meta setup

1. Sign in to Meta Horizon Developer Dashboard with Alex's developer account.
2. Create or select the QuestFlight application using package
   `com.alexoviedo.t2.questflight`.
3. Upload the immutable candidate through Meta Quest Developer Hub to the
   private **Alpha** channel.
4. Add truthful Alpha metadata: app name, description, and header image. Saving
   metadata does not require submitting the application to the public store.
5. Open **Distribution > Release Channels > Alpha > Channel Settings**.
6. Set channel access to **Private**.
7. Enable **Grant access to users by URL**.
8. Select **Automatic approval** and retain the default 200-user limit.
9. Save changes and copy the generated invitation URL.
10. Test the URL with one Meta account that is not an organization member.

Private-channel testers must be at least 18. The URL invitation page is
disabled 90 days after the channel's latest upload; uploading a newer signed
build re-enables it. Future APKs must use the same signing identity and a higher
Android version code.

## Friend-facing steps

Send only the Meta invitation URL and these instructions:

> Open this link with the Meta account used by your Quest 3 and tap Join. On
> the headset, open App Library, select Not Installed, choose QuestFlight, and
> tap Install.

Developer mode, GitHub access, USB, ADB, and SideQuest are not required.

## Maintainer fallback

The private GitHub prerelease remains available for maintainers who need exact
debug-signed checkpoint recovery. It is not the recommended friend workflow.
Because the GitHub and Meta artifacts use different signing certificates, one
cannot update the other in place despite sharing the same package identifier.

## Authoritative references

- [Meta: add users to a release channel](https://developers.meta.com/horizon/resources/publish-release-channels-add-users/)
- [Meta: Android application signing](https://developers.meta.com/horizon/resources/publish-mobile-app-signing/)
