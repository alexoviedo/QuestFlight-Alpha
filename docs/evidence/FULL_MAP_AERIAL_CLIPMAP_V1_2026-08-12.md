# Full-map aerial clipmap V1 evidence — 2026-08-12

## Scope and source

- Branch: `feature/full-map-aerial-clipmap-v1`
- Source commit: `203830988eb705972da683ec620e8608d6482dfa`
- Product extent: 24 km × 24 km in the existing KBDU-local ENU/AEQD frame.
- Imagery: City of Boulder / Pictometry 2020 cached nine-inch imagery, ArcGIS item
  `22b7c93a74394696824258f807f345ba`, published at 0.2286 m and recorded as CC0.
- The separately evaluated 2024 ImageServer exports were rejected because their
  independent pages contained visible radiometric blocks. No 2024 page is present
  in the accepted manifest or Android bundle inventory.

This is visual context for a personal simulator. It is dated imagery, not current,
survey-grade, or suitable for navigation.

## Offline compilation

The deterministic compiler produced:

| Tier | Grid/pages | Inner resolution | Page | Runtime window |
| --- | ---: | ---: | ---: | ---: |
| High | 36 × 36 / 1,296 | 0.333333 m/texel | 2048² + 16 px gutter | 3 × 3 |
| Medium | 24 × 24 / 576 | 1.0 m/texel | 1024² + 8 px gutter | 3 × 3 |
| Global | 1 | 5.859375 m/texel | 4096² | 1 |

- Pages audited: 1,873/1,873.
- Page-manifest SHA-256: `98129cbf175c218d63d04365883fb670cda6daddf07fccfeb047581047e7ea6a`.
- High-tier seam comparisons: 2,520; mean absolute RGB 2.4265; p95 of seam means 3.4523.
- Medium-tier seam comparisons: 1,104; mean absolute RGB 2.1450; p95 2.7595.
- Full-map mosaic and airport-centered high-resolution stitch were inspected; no
  independent export-page blocks were accepted.

The Android compiler produced 469 LZ4 AssetBundles containing exactly 1,873 page
assets and 1,294,809,265 payload bytes. The complete inventory and per-bundle
hashes are in `android_bundle_inventory.json` under the artifact root. Generated
pages, bundles, raw source data, and the APK remain outside Git.

## Runtime architecture and budget

One explicitly serialized `AircraftCenteredAerialClipmapController` is owned by
the aircraft simulation root. It selects aircraft-centered 3 × 3 high and medium
windows, loads into staging `Texture2DArray` objects, and swaps only a complete
window. It does not construct hierarchy, make runtime network requests, or write
the OpenXR camera. A one-cell-capped velocity lookahead prefetches along the flight
path. The accepted fixed 2020 pyramid remains the startup fallback and releases
its explicit mip requests only after global, medium, and high tiers are complete.

- Android page format: ASTC 10×10, mipmapped, clamped, trilinear, anisotropy 8.
- Active + staging arrays + global texture: 23,889,952 bytes (22.783 MiB).
- Terrain draw/material count: unchanged; high and medium arrays share one sampler.
- Runtime loading is local from packaged Android AssetBundles.
- APK: 1,522,653,200 bytes; SHA-256
  `290a1dd2057a2045264302289ab0c824809c5c8c28cdb4d0e264c7c608b75cb5`.

The APK is intentionally large because the complete 24 km square is available
offline. The runtime residency is bounded independently of that package size.

## Validation

- Python compiler/manifest tests: 7/7 passed.
- Android inventory: 1,873/1,873 pages and 469/469 bundles passed; no 2024 assets.
- EditMode: 323/323 passed.
- PlayMode: 10/10 passed.
- Product dependency closure: zero rejected and zero unknown dependencies.
- Android ARM64/Vulkan/OpenXR development build: passed.
- Deterministic Visual QA: 118 captures produced and inspected; no observed
  terrain, runway, water, cockpit, building, mountain, or aircraft regression.
- Machine Visual QA aggregate remains failed because the inherited scene exceeds
  two planning thresholds: approach view 80 draws versus 75 and cloud-ground view
  668,039 triangles versus 650,000.
- Static planning budget remains failed: 80/75 max draws, 675,865/650,000 max
  visible triangles, 38/29 materials, 282,631,142/220,200,960 estimated static
  texture bytes, and 17/3 transparent material slots. These are aggregate scene
  limitations and were not relaxed for this work.
- No final Quest frame-time or thermal acceptance is claimed.

The exact development APK was installed successfully on Quest 3. An unattended
launch could not produce valid runtime clipmap evidence because the unworn headset
immediately lost XR focus; an older persistent trace containing rejected 2024
proof assets was explicitly identified as stale and excluded. Exact-APK worn
activation and moving-page trace remain the final acceptance item.

## Limitations

- The 2020 source is coherent with the project's existing authored objects and
  clean plates but is not current imagery.
- High detail follows the aircraft only inside the existing 24 km square; this
  milestone does not expand world coverage.
- AssetBundles are ignored deterministic local build products, so a fresh clone
  must run the documented offline compiler before building the complete APK.
- The Editor cannot render Android-native AssetBundles and therefore exercises
  the unchanged startup fallback in deterministic captures.
- This evidence does not claim photorealism, navigation accuracy, FAA approval,
  training credit, or accepted Quest performance.
