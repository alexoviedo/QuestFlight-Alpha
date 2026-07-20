# Building coverage report

Status: source coverage, grounding, HLOD accounting, and static budgets pass; model-quality acceptance fails.

The bounded source is a 2026-05-31 OpenStreetMap Overpass snapshot. Raw response SHA-256 is `024560100b28946bb805478c6be091d752172200e7c8fcbe1ebe1d7bea858416`; it remains external. The normalized ODbL-1.0 derivative retains `© OpenStreetMap contributors` attribution.

| Zone | Valid source footprints | Represented | Significant represented | Required | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| 0–2 km | 1,831 | 1,831 (100%) | 1,663/1,663 | 100% | PASS |
| 2–4 km | 7,863 | 7,863 (100%) | 7,668/7,668 | ≥95% | PASS |
| 4–6 km | 17,985 | 17,985 (100%) | 17,450/17,450 | ≥90% | PASS |

All 27,679 footprints have stable IDs and are mapped exactly once through 1,224 logical 250 m source cells. Runtime generation uses shared building materials and deterministic height/roof/facade variation. The corrected offline HLOD recipe retains complete source mapping while grouping serialized renderers; it does not create one GameObject per source building.

The authoritative terrain placement pass reports building root-to-best-fit-plane median/p95/max errors of 0.000/0.000/0.000 m. Footprints with more than 0.15 m residual are marked `FoundationSkirt`; source footprints and residual values remain in the placement asset. Machine placement is not evidence that the procedural building art looks realistic in-headset.

Final serialized HLOD/static budgets pass. Full-resolution inspection finds that the complete coverage reads as a large field of flat grey/basic extrusions with weak facade, roof, and contact variation; building quality scores 4/10 despite coverage scoring 8/10. Stereo LOD stability and physical Quest timing were not run because the lead visual gate failed.
