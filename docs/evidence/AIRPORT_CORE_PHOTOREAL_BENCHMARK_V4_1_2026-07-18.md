# Airport Core Photoreal Benchmark V4.1 — failed remediation checkpoint

## Decision

`FAIL — PRESERVE V4.1 BRANCH, DO NOT MERGE OR SCALE`

V4.1 was a bounded remediation of the rejected V4 airport-core benchmark. It did not add reference assets or expand airport coverage. The controlled lighting recovery did not satisfy the V4.1 one-atlas remediation contract, and the integrated scene remains above the serialized triangle ceiling. Main, Meta XR Simulator, an Android build, and the physical Quest gate were therefore not entered.

## Exact source and evidence boundary

- Branch source: `feature/airport-core-photoreal-v4` at `2d7355cc8bffa8709478565aa514328f3ba4dd51`
- Working branch: `feature/airport-core-photoreal-v4-1`
- Artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_core_photoreal_v4_1_20260718_000938`
- Unity: `6000.3.8f1`
- Evidence is Editor/static authoring evidence only. It does not establish stereo quality, visual acceptance, comfort, Android build integrity, or Quest performance.

## Remediation implemented

- Quarantined the rejected V4 lighting data and four prior baked-lightmap textures: two color EXRs and two directional PNGs.
- Serialized neutral Trilight ambient lighting while retaining the CC0 HDR only for visible sky and reflections.
- Restored the stable V2 sun direction for the low-confidence recovery baseline and synchronized sky/water rotation.
- Added an explicit Meta pass and bounded realtime irradiance paths for the airport buildings, terrain, runway, markings, water, and vegetation.
- Replaced the striped reservoir normal inputs with the existing committed broad-wave/ripple textures and reduced wave/shore/glitter strength.
- Replaced duplicate serialized V2/V4 context renderers with one active filtered LODGroup plus editor-only mesh-path provenance.
- Combined six apron-prop LOD sets into three renderer assets, integrated the existing project-owned fuel-service LOD set, and removed the superseded generic `AirportArtProduction` hierarchy.
- Serialized 18 light probes at two heights.
- Preserved a fail-closed realtime fallback: the scene has no LightingData reference and no accepted lightmap asset.

## Controlled bake result

Both controlled GPU Progressive Lightmapper attempts used:

- neutral Trilight environment input;
- only terminal LOD0 and Type 2 hangar LOD0 in the `ContributeGI` allowlist;
- non-directional lightmaps;
- 1024 maximum atlas size;
- 18 probes at two heights.

Attempt 1 at lightmap chart scale `0.55` produced two non-directional 1024 atlases. An intermediate integration retry failed before baking because no triangles intersected the V4 replacement masks; the idempotency error was corrected without changing art. Attempt 2 at scale `0.25` also produced two non-directional 1024 atlases. The lightmapper did not emit the prior non-finite diagnostic in either completed attempt, but pixel-level finiteness validation was not reached because atlas-count validation failed first. Both outputs were rejected and deleted because the bounded V4.1 remediation contract requires exactly one atlas. No bake manifest, LightingData asset, or lightmap texture is accepted in the product tree.

## Static characterization

Airport-core structural inventory after consolidation:

- 15 serialized benchmark renderers;
- 77,282 serialized benchmark triangles;
- 3 benchmark materials;
- terminal LOD0/1/2: 20,172 / 8,592 / 2,364 triangles;
- hangar LOD0/1/2: 25,492 / 10,244 / 3,444 triangles;
- 0 transparent benchmark slots;
- 18 validated benchmark textures.

Product integrity after quarantine:

- 171 serialized renderers — within 180;
- 759,814 serialized triangles — **fails 700,000 by 59,814**;
- 32 serialized materials — within the product integrity ceiling;
- no unresolved GUID remains.

The airport-core structural validator correctly returns `FAIL_AIRPORT_CORE_V4_1_STRUCTURAL_GATE` because the manifest has no accepted lightmap. The full visible-draw/material/texture-memory gate was not rerun after the bake failure, so no current pass is claimed.

## Validation actually run

- Unity batch compile: initial trivial `Renderer`/`MeshRenderer` error, isolated correction, retry passed.
- Controlled integration/bake attempt 1: lightmapper completed without the prior non-finite diagnostic; rejected for two atlases before pixel validation.
- Intermediate integration retry: failed before baking because no triangles intersected the replacement masks; the isolated idempotency correction was applied.
- Controlled integration/bake attempt 2: lightmapper completed without the prior non-finite diagnostic; rejected for two atlases before pixel validation.
- Airport-core structural validator: failed as expected because no accepted lightmap is bound.
- Product integrity validator: failed only the serialized triangle ceiling after the dangling rejected-lighting GUID was cleared.
- EditMode: not run after the gate failed.
- PlayMode: not run.
- Production Visual QA: not run; no post-remediation score is claimed.
- Meta XR Simulator: not run.
- Android Quest build: not run.
- Physical Quest: not run.

## Remaining blockers

1. Terminal and hangar still pack into two 1024 non-directional atlases; the one-atlas contract is unmet.
2. Serialized product geometry remains 59,814 triangles above the 700,000 integrity ceiling.
3. The corrected realtime visual presentation has not been captured or reviewed, so no quality score is available.

## Exact next bounded milestone

Run an **Airport-Core V4.2 static packing and geometry closure** with no art, material, shader, lighting-color, or coverage changes. Repack the existing terminal/hangar lightmap UV2 charts into one 1024 atlas, identify the measured triangle contributors, and remove or decimate at least 59,814 serialized triangles while preserving runway-corridor samples. Stop before Visual QA unless both structural gates pass. Alex and the headset are not required until a later machine visual, simulator, APK, and static-budget gate succeeds.
