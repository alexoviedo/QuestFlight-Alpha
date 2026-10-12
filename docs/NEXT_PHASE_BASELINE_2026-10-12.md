# QuestFlight next-phase source baseline

The reusable remediation source is consolidated on local `main`. The audit
package is intentionally ignored and remains available locally; it is not
shipping Unity content. Detailed findings and gates remain in
`REMEDIATION_HANDOFF_2026-10-09.md` and `REMEDIATION_STATUS_2026-10-09.json`.

The approved exterior authorer combines 16 static C172 pieces into one authored
mesh for external views, preserving the cockpit's original renderers and the
showroom visibility owner. Its source and tests are committed. The generated
aircraft derivative, dependent prefab bindings, previously held mesh repairs,
SDK material and personal DevAgentSettings remain in a local product overlay.
`AGENTS.md` requires unclear-rights aircraft assets to remain uncommitted. The
overlay is preserved outside the clean source checkout; it must not be added to
Git or mistaken for a release-rights grant.

Local preservation root:
`C:/QFBackup20261008/CLOSEOUT_20261012T185048Z`.
`overlay_manifest.json` lists every held path, byte count and SHA-256;
`local_product_overlay/` contains the exact files and paired metas.
`all_local_changes_before.zip` preserves all pre-consolidation changes, and
`repository_before.bundle` preserves the original refs and source history.
The full authored product remains staged in `C:/QFRC20261008/QuestFlight` for
the current Android build. It is a detached build worktree with an explicitly
recorded source overlay, not an independent development branch.

Build the identified staged product with:

```powershell
pwsh -NoProfile -File scripts/build_production_quest.ps1 -ProjectPath C:/QFRC20261008/QuestFlight -PreflightOnly
```

A real build requires the current preservation, disk guard, artifact/source
binding and private-certificate inspection recorded in the latest handoff.
Restore the local overlay explicitly before building from a fresh source
checkout. Do not implement hierarchy reconstruction at runtime. The offline
authoring entry point is
`QuestFlight.Editor.ProductionAircraftExteriorBatchAuthoring.RunBatch`;
generation still requires the exact local source assets and provenance checks.

Retained standard QA is 107/130 passing, with 23 unresolved triangle-budget
failures and the side-window draw excess. Ten focused tests passed and three
geometry/visibility cases passed again against the saved prefab. The subsequent
nine-fixture planar diagnostic rejected the proposed optimization: even its
relaxed upper bound cannot resolve any of the 23 failures. These results are
not physical frame-rate, stereo, comfort or flight-model acceptance. Keep the
existing budgets and individually recorded failed/unverified gates.

Native JSBSim remains the sole flight authority. OpenXR owns the Main Camera.
The authored hierarchy, standalone Quest 3 target, GUIDs and user data remain
preserved. No dependency upgrade, renderer rewrite or aircraft parameter change
is part of this consolidation.

The current APK is built and installed: `2352c48108d984cda31a2e1b77227175041f20ebf78bfdffc63a03df0fa2676b`. Packaging12/12 and exact installed hash/sandbox continuity passed. Main checkout cleanup was completed by the owner; its receipt is in the preservation root. The latest handoff records source identity, local overlay, full progress archive and independently open acceptance. Do not replay completed cleanup/install scripts. Personal DevAgentSettings remains archived; shipping/staged settings must follow the existing disabled/empty-credential release policy.
