# Product asset policy

## Admission rule

Every non-code asset must have all of the following before it enters Git:

1. identifiable creator and canonical source;
2. explicit current license or public-domain basis;
3. exact SHA-256 and byte length;
4. required attribution/change notices;
5. a reviewed Unity `.meta` file and stable GUID;
6. confirmation that its inclusion does not add a legacy runtime dependency;
7. inspected Visual QA for any visual change.

Classification:

- **A — redistributable:** clear product redistribution rights, with documented conditions.
- **B — local-only:** personal-use, nonredistributable, conflicting, missing, or unclear terms. Never commit the binary.

Unclear means B. A mirror's word `free` is not a license. A hash mismatch is a new asset and returns to the gate.

## Binary handling

- Rights-approved binary formats use Git LFS according to `.gitattributes`.
- Raw downloads, archives, caches, captures, APKs, and source geospatial responses stay outside Git.
- Never add a binary with `git add` until `git check-attr filter -- <path>` reports `lfs` where applicable.
- Inspect staged LFS pointers and run `git lfs ls-files` before commit.
- Preserve paired Unity `.meta` files. Never regenerate a referenced GUID casually.
- Do not store signing keys, credentials, or account-bound marketplace assets.

## Aircraft policy

The exact KOG_THORNS `Cessna172` GLB listed in `docs/aircraft_rights_report.md` is classification A under CC BY 4.0. Only its pinned hash is approved.

- Development sync is offline and explicit through `scripts/sync_local_aircraft_asset.ps1`.
- Runtime code may not download, locate, substitute, or rebuild an aircraft hierarchy.
- Missing or mismatched aircraft content must fail Editor validation and the product build with the expected path, hash, and sync command.
- A project-authored placeholder may be used only if it is explicitly serialized and clearly labeled; it must not hide a missing production asset or silently pass the visual gate.
- Any new aircraft revision, conversion, texture substitution, marketplace model, or generated replacement receives a new report.
- CC BY does not grant aircraft-manufacturer trademark rights. Avoid endorsement claims and review/remove brand marks before public commercial release.

## Geospatial policy

- Commit only bounded, optimized derivatives required by the authored product scene.
- Preserve the pinned FAA/BTS, USGS, and OSM snapshot date, requests, hashes, and coordinate frame.
- Keep `© OpenStreetMap contributors` and an ODbL link visible in repository notices and in a user-accessible product credits/about surface.
- When publicly distributing a Produced Work, also offer the OSM derivative database in machine-readable form as required by ODbL.
- Public-domain government data still receives source credit and no endorsement claim.
- No Google Maps, Google Earth, Street View, or asset derived from their pixels or captures.
- No raw DEM, orthophoto, OSM response, or archive in Git.

## Generated assets

Generated meshes, materials, textures, and prefabs must record:

- generator/tool version or frozen source commit;
- all input datasets and license boundaries;
- deterministic parameters when practical;
- output SHA-256;
- whether the output is a database derivative, a Produced Work, or entirely project-authored.

The production macro albedo is a project-authored Produced Work derived from pinned USGS/OSM facts. It is not aerial imagery and must not be described as photorealistic orthophotography.

## Release gate

Before each release:

1. enumerate repository binaries and compare them with `docs/ASSET_LICENSES.md`;
2. verify hashes and LFS pointers;
3. verify notices are included in the repository and user-accessible credits;
4. verify the OSM machine-readable derivative offer;
5. reject restricted, unclear, unrecorded, or hash-mismatched content;
6. run product validation, Visual QA, PlayMode tests, and the production Quest build.
