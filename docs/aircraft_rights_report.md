# Aircraft rights report

Audit date: 2026-07-12

Frozen R&D source: `cd4131fa7732a6db2d7387921dfef33e2c206970`

Decision: **A — redistributable**, subject to CC BY 4.0 attribution and the product conditions below.

This is a provenance and product-policy review, not legal advice.

## Asset identity

| Field | Verified value |
| --- | --- |
| Title | `Cessna172` |
| Creator | `KOG_THORNS` (`@ioai25312`) |
| Original source | <https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4> |
| License | Creative Commons Attribution 4.0 International (`CC-BY-4.0`) |
| Frozen GLB SHA-256 | `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872` |
| Frozen GLB size | `26,962,216` bytes |
| Frozen GLB importer meta SHA-256 | `caf371d9b2ab80ae89d3d69a7eb5f257ebf3d17cf4eaad171084ed63384327ab` |
| Frozen mirror note SHA-256 | `27c74a001217b2213fecd5cc3d36b64246bcec46013d3219f7416a216a8e8973` |

## Evidence chain

1. The R&D `SOURCE.txt` records a download from DownloadFree3D and deliberately treats it as a personal placeholder pending a rights audit. The mirror page names the archive `cessna172-made-by-kog-thorns`, but does not display an asset-specific license. It is not the licensing authority used for this decision.
2. The frozen GLB is a Sketchfab export (`Sketchfab-16.28.0`). Its embedded `asset.extras` identifies:
   - author `KOG_THORNS (https://sketchfab.com/ioai25312)`;
   - license `CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)`;
   - source model ID `d1b15841c29c43d0862667300bad55a4`;
   - title `Cessna172`.
3. On 2026-07-12 the live original Sketchfab page still identified KOG_THORNS as creator, offered the model for download, and displayed `CC Attribution` as the license.
4. Sketchfab's current Terms of Use section 4.2.2 recognizes free User Content distributed under Creative Commons licenses and requires downloaders to follow the license displayed for that content. No contrary asset-specific restriction was found.
5. The current CC BY 4.0 legal code grants reproduction, sharing, and adaptation in any medium, including technical format changes, subject to attribution. The license is worldwide, royalty-free, non-exclusive, and irrevocable while its conditions are met.

Primary references:

- Original model: <https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4>
- Creator profile: <https://sketchfab.com/ioai25312>
- Sketchfab Terms of Use: <https://sketchfab.com/terms>
- CC BY 4.0 deed: <https://creativecommons.org/licenses/by/4.0/>
- CC BY 4.0 legal code: <https://creativecommons.org/licenses/by/4.0/legalcode.en>
- Secondary mirror record: <https://downloadfree3d.com/3d-models/aircraft/airplane/cessna-172-3/>

## Product conditions

The exact audited GLB may be copied and redistributed in the product when all of the following remain true:

1. `THIRD_PARTY_NOTICES.md` ships with the repository and product notices.
2. Credit identifies `Cessna172` and KOG_THORNS, links the original model and CC BY 4.0, and does not imply endorsement.
3. Any modification to the licensed material is identified. The current GLB binary is unchanged; the product adds separate Unity hierarchy, visibility/lighting treatment, authored control pivots, and a symmetry-derived four-pedal runtime mesh documented by `Assets/Production/Cockpit/Pedals/C172_FourPedalAssemblyManifest.json`.
4. No DRM or additional legal term prevents recipients from exercising CC BY rights in the licensed material.
5. Only the exact SHA-256 above enters the product. A new download, revision, conversion, or replacement returns to the rights gate.
6. The binary is stored through Git LFS with its original Unity `.meta` GUID preserved.

Canonical product path:

`QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb`

Use `scripts/sync_local_aircraft_asset.ps1` to validate and copy the frozen source. The product must never download, discover, or reconstruct an aircraft at runtime. A missing or mismatched asset is an Editor/build failure with an actionable message.

## Rights not resolved by CC BY

CC BY 4.0 expressly does not license trademark or patent rights and provides no warranty of title or non-infringement. The model depicts a Cessna 172 and contains Cessna-identifying visual material. Therefore:

- do not claim affiliation, sponsorship, endorsement, certified fidelity, FAA approval, or training suitability;
- retain the neutral `C172-style` product wording where practical;
- obtain product/legal review or remove/rework brand marks and distinctive livery before a public commercial release.

This trademark caveat does not change the copyright classification: the exact audited model is **A — redistributable under CC BY 4.0**.
