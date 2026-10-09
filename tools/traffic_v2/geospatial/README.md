# Traffic V2 geospatial source gate

This directory contains the deterministic Phase-3 authority freezer and
compiler. It never commits source payloads. ArcGIS/Overpass responses,
metadata, request bodies, receipts, normalized source partitions,
intermediates, and the coverage PNG remain beneath the explicitly supplied
external run directory.

The compiler writes only the four Phase-3 text artifacts to
`docs/data/traffic_v2`; `traffic_source_coverage.png` remains external and is
bound by SHA-256 from both the coverage report and run manifest.

The v2 registry/compiler correction is fail-closed on every raw record:

- exact duplicate OSM type/ID occurrences are counted once, while inconsistent
  repeats stop normalization;
- one exclusive external-package operation lock serializes inventory,
  acquisition, compile, validation, and rebuild; each command repeats its
  complete external-state preflight after taking the lock. Compile, validate,
  and rebuild additionally share a machine-global canonical target-set lock,
  independent of `TEMP`/`TMP`, so aliases or separate external packages cannot
  concurrently write or inspect the same data/evidence roots;
- bounded acquisition retries HTTP failures and ArcGIS retryable error codes
  429/5xx even when a server incorrectly returns the error JSON with HTTP 200;
  semantic 4xx errors remain immediate fail-closed results;
- every frozen ArcGIS OID and every unique raw OSM way has an explicit
  normalization and motor-road eligibility disposition;
- one shared correspondence predicate governs cross-source suppression,
  class/lane donation, independent support, and road-reference matching. It
  requires shared declared identity plus compatible direction/orientation,
  carriageway, effective motor access, bridge, tunnel, layer, junction, and
  endpoint-grade semantics. Donation may fill only its named missing field and
  requires exact known compatibility in the other direct evidence field.
  Exact XZ geometry alone is never identity proof;
- City `ONEWAY` B/FT/TF and OSM declared/default directions are explicit.
  City N/null and the undocumented County B/F/T code meanings remain
  fail-closed; parallel, opposing, stacked, or otherwise ambiguous roads are
  retained and block the gate;
- the canonical universe, source-separated conflation ledger, independent
  cross-source reference ledger, lane-evidence ledger, and unmatched-reference
  inventory remain external and content-hashed;
- independent lane-km corroboration builds one deterministic support-geometry
  `STRtree`, buffers each canonical segment once, and evaluates exact
  correspondence only for sorted nearby candidates. Full compiles report this
  stage's processed/total records, elapsed time, queried candidates, and exact
  intersections at least every five seconds;
- reference-to-canonical representation builds one deterministic canonical
  `STRtree`, evaluates both authority-inclusive and different-source results
  from the same sorted conservative candidate set, and reports processed/total
  progress at least every five seconds. It never rebuilds or linearly scans the
  full canonical list for each reference observation;
- canonical, lane, and independent-support rows are released immediately
  after their exact files are serialized, before the large reference phase;
  acquisition leaves the compile-owned `approval_candidate` path absent;
- the representation-coverage denominator is unique eligible source-priority
  canonical lane-kilometres, and its numerator is the explicitly represented
  canonical lane-kilometres. A correctly dispositioned bounded OSM-only
  fallback road therefore counts as represented; a redundant second source is
  not required. Different-source subspan support (including
  precedence-suppressed records) is a separate non-gating corroboration metric.
  Corroboration enters that metric only with the same direct eligible class and
  lane count, and semantic conflicts still block. There is no redundant
  source-observation-count threshold. The 95% local and 100% major-class gates
  compare unrounded deterministic numerator/denominator values;
- lane-kilometres are line length times a direct, record-bound lane count.
  Unknown class/lane/correspondence, unresolved geometric length, and
  geometryless motor-road records remain explicit blocking denominator/audit
  entries; road width is never converted to lanes;
- OSM whole-way and per-lane motor access use
  `motorcar > motor_vehicle > vehicle > access`, including directional and
  conditional variants. Lane-token cardinality must agree with static total
  and directional lane declarations. Only a complete all-`no` declaration can
  exclude; mixed, restricted, conditional, unmapped, or contradictory evidence
  is `UNKNOWN_BLOCKING` and cannot donate or enter known lane-km. Mode-specific
  oneway tags use the same specificity discipline; conditional/unmapped
  direction never falls through to an ordinary bidirectional default. A
  `lanes:both_ways` slot is independently resolved through forward and backward
  mode-major branches and becomes known only when both outcomes are identical.
  Unqualified `service` roads block unless an explicit subtype/access decision
  applies;
- current orthophoto ambiguity review is bound to official USDA FPAC sources
  only. The freezer captures the exact 994,968-byte Colorado 2021 NAIP
  quarter-quad coverage archive, the USDA page that labels that archive as the
  most recent Colorado inventory, and the USDA FPAC public-domain statement.
  The archive SHA-256 and ZIP signature, the independently frozen ordered 25
  quarter-quad record numbers, the complete statewide record count, the exact
  KBDU `[-12000,+12000]` metre coverage envelope, and the prevalidated union
  bounds are pinned in the registry. Archive bytes, inventory hash, coverage
  page, and license are semantically recomputed from the frozen payloads during
  every closure validation. Only that in-memory verified closure result can
  clear the temporal blocker during compile and staged validation; a stored or
  fabricated PASS summary cannot. No `exportImage`, tile, JP2/TIFF, or
  orthophoto pixel payload is requested or stored; the small quarter-quad ZIP
  is source metadata and remains external. USDA coverage and license HTML are
  parsed structurally from visible
  `body` content only: comments, `head`/`title`, content outside `body`, and
  every `script`, `style`, `template`, or `noscript` subtree are non-visible.
  Any `base` element anywhere rejects the document instead of changing URL
  resolution. Interactive, embedded, fallback, and foreign-content scopes
  such as `details`, `dialog`, `iframe`, `object`, `embed`, `canvas`, `svg`,
  `math`, media, `meter`/`progress`, and form-option/text controls can never
  supply authority evidence, regardless of their attributes or open state.
  Boolean `hidden` is inherited through descendants, and any element with a
  nonempty inline `style` attribute makes its complete subtree unusable as
  authority evidence. Structural errors are explicit and must be empty;
  multiple or nested bodies/rows, out-of-order body/table/row closure, stack
  corruption, and unclosed critical, hidden, anchor, or non-authority scopes
  fail closed while ordinary optional `tbody`/`td` end tags remain valid. The
  visible coverage table must have one Colorado row and one active quarter-quad
  anchor. Its undecoded raw `href` value must equal
  the registry-pinned USDA-relative path, its parsed value must equal that raw
  value, its resolved URL must equal the separately pinned official USDA HTTPS
  ZIP URL, and its normalized visible label must be exactly
  `Shapefile (zip, 442KB)`. Malformed visibility markup, character-reference
  aliases, and alternate, stale, duplicated, queried, fragmented, or
  differently labelled links fail closed;
- NAIP is `VISUAL_AMBIGUITY_RESOLUTION_ONLY`. It can never create or move a
  road centerline, supply a lane count or lane direction, assign a road class,
  or enter canonical precedence/reference ledgers. The City 2023 NAIP layer
  may corroborate a central-area review but its extent cannot clear the
  complete-bounds gate. Blank-license City 2024 imagery and purchase-only
  DRCOG imagery remain excluded;
- the registry pins the complete CPython 3.13.13 environment, requirements-lock
  bytes, ABI/platform, complete installed distribution name/version set,
  every non-cache RECORD-owned distribution payload, runtime/stdlib/DLL
  payload, exact gate import origins, and GEOS/PROJ/zlib versions. It also
  requires the `python -I -B` launcher flags, canonical interpreter search
  path, disabled user site, exact encoding/locale/OS/native identity, absence
  of unbound Python/PROJ startup variables, disabled PROJ networking, and an
  exact RECORD-closed site-packages/PROJ data tree. Any executable owned
  `.pth` is byte-bound; unowned `.pth`, startup hooks, and stray modules fail
  closed. Acquisition
  creates or exact-compares the canonical external
  `toolchain/traffic_v2_geospatial_toolchain_manifest.json`; freeze, compile,
  validate, rebuild, D verification, and the run context all bind it;
- the outer `package_index.json` is the canonical byte serialization of one
  pure live expected-index value. It binds every active external file, rejects
  duplicate/conflicting/fabricated records and alternate field/order/type
  spellings, and labels archived rejected attempts, quarantined files, and the
  pinned toolchain as non-authority partitions. Rebuild derivatives and their
  semantic run/report closure are revalidated before indexing;
- compile and rebuild write only to isolated staging trees after full frozen
  byte-closure validation, including every canonical external or repository
  file record. Before promotion, compile reparses all four text artifacts,
  recomputes the canonical lane-km and 576-cell inventories, independently
  re-derives reference match decisions and canonical evidence conflicts from
  their exact normalized/conflation/lane/support ledgers, cross-validates
  blockers/missing-road/provenance/candidate/run-manifest bindings, and checks
  the exact PNG. Candidate comparison and live approval validation happen
  before a rollback-capable promotion; the candidate directory is create-once
  by directory rename with its manifest written last.

Use the exact external environment recorded by `requirements-lock.txt`:

```powershell
$python = 'C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_system_v2_20260821_170722\workstream_a\toolchain\py313-env\Scripts\python.exe'
$run = 'C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_system_v2_20260821_170722\workstream_a'
$evidence = Join-Path $run 'evidence'

& $python -I -B -m pytest tools\traffic_v2\geospatial\test_source_gate.py -q -p no:cacheprovider
& $python -I -B tools\traffic_v2\geospatial\source_gate.py inventory --external-run $run
# Run acquisition only when the Lead authorizes a new exact source freeze.
& $python -I -B tools\traffic_v2\geospatial\source_gate.py acquire --external-run $run
& $python -I -B tools\traffic_v2\geospatial\source_gate.py compile --external-run $run --evidence-dir $evidence
& $python -I -B tools\traffic_v2\geospatial\source_gate.py rebuild-check --external-run $run --evidence-dir $evidence
& $python -I -B tools\traffic_v2\geospatial\source_gate.py validate --external-run $run --evidence-dir $evidence
```

Acquisition is fail-closed. A required endpoint, item-level license,
count/ID-stability check, complete page, or accepted-product pin failure stops
the freeze. OSM is a separately attributed ODbL partition and is never merged
into a differently licensed source shard. The compiler clips all geometry
offline to the exact KBDU-centered AEQD `[-12000,+12000]` metre square.

The first corrected compile intentionally writes immutable candidate artifacts
that report
`PENDING_LEAD_D_INSPECTION`. It preserves the exact five candidate artifacts
under external `approval_candidate/` and binds the same canonical-universe hash
and count to all 15 frozen benchmark views. A later compile can clear that
single review hook without reacquisition only when the separately owned
`tools/traffic_v2/schemas/traffic_v2_geospatial_universe_approval_v1.json`
binds the exact universe, camera contract, source freeze, all five candidate
artifact hashes, and live structured `{path,bytes,sha256}` witnesses for the
passing D verifier, D Markdown report, machine-readable D result, and Lead
full-resolution inspection. Their exact
registry-owned paths are
`tools/traffic_v2/validation/verify_current_source_gate.py`,
`docs/evidence/traffic_v2/TRAFFIC_V2_CURRENT_SOURCE_GATE_VERIFICATION.md`, and
`docs/evidence/traffic_v2/TRAFFIC_V2_CURRENT_SOURCE_GATE_VERIFICATION.json`, and
`tools/traffic_v2/schemas/traffic_v2_geospatial_universe_inspection_v1.json`.
Witness paths must be canonical repository-relative paths; live existence,
byte length, lowercase SHA-256, and content-derived PASS are revalidated during
approval evaluation and output validation. The Markdown must contain exactly
`<!-- TRAFFIC_V2_PHASE3_SOURCE_GATE_DECISION: PASS -->`. The D JSON must bind
the live source tool, registry, lock, toolchain manifest, freeze, camera,
universe, candidate manifest, five artifacts, verifier/report records, exact
reproduction command/zero exit, and every registry-required check. The Lead
JSON must bind the same candidate/freeze/camera/universe and prove that the
bound 1600x1600 PNG was opened full-resolution by a named inspector after the
canonical candidate/freeze event time and not with a future-dated
timestamp. Generic
or outer PASS labels never substitute for those live semantics. Approval never
rewrites coverage, conflicts, missing roads,
provenance, or PNG bytes; live approval and the effective gate are recorded
only in the lead-owned approval record, external run manifest, and outer
package index. Repeated pending/approved compiles compare all five bytes and
fail on divergence. Rebuild-check writes only below `rebuild_check/` and cannot
create or mutate `approval_candidate/`.

The older `verify_phase3_source_gate.py` and `TRAFFIC_V2_PHASE3_SOURCE_GATE_VERIFICATION.*`
remain historical failed-freeze investigation records; they are not current
approval witnesses. The registered current verifier accepts the exact generated
`--registry/--external-run/--data-dir/--evidence-dir` command, revalidates frozen
toolchain/input bytes, independently recomputes ledger summaries, and reproduces
the five candidate artifacts in isolated scratch space. Missing pinned packages,
changed inputs or non-camera authority blockers produce `FAIL_CLOSED`; the
verifier does not generate a human full-resolution inspection. A changed source
tool or registry requires a newly authorized freeze, never relabeling old bytes.

Full-map schedule generation separately verifies exact count-page membership,
bytes, hashes, coordinate contract, registry and per-source license partitions.
Count input identity is carried into the generated manifest and provenance.
Development candidate modes report `all_576_source_cells_evaluated=false` until
an actual complete unique 24-by-24 evaluated inventory is bound; a candidate path
does not establish complete review.

OSM semantic interpretations are pinned to the official OSM Wiki access,
oneway, service, and service-road pages recorded in `source_registry.json`.
Those documentation records are CC BY-SA 2.0; derived OSM road data remains a
separate ODbL 1.0 partition.

The rejected freezes `fff0906c8cc1deb615741ea5d56ed80ee59b2e9e524b5bd70af842f345e4cd23`,
`178ac781ea094e4f09af9a38493babace8f78e8942301b86927e414f2675ca3d`,
`d90132cf96e919406f52c32abbc19bf0b1936d3578ad0ace29cc693ca2e06817`,
`f5fa99a5a607054991caf1a9fd9ebb6f2bdb0bd6380cfd80587a1d42379c5ad1`,
and `c7cd7b9362d01bacbeb7c23833cae65882bceaaaab767dcb00f9b8c0904e07b6`
and `e371e027d1ef9f2fa3b7a6f534e0e23842e8f63a5cc6b7202eee85bb82eb7dc2`
and `80ccf33a89eb718ffb2e64755f41df6d900d39bca8894e790bf7fde1395d9306`
and `2211980332dbdb4e09bcbba0a38350ced92f7f14086885f1066cebdc7cc71501`
remain failed or superseded attempts. Archived trees use Windows
extended-length syscalls for topology, hashing, and package indexing, so their
original receipt names remain preserved beyond the legacy `MAX_PATH` limit. A
newly authorized acquisition archives
the currently active rejected package under the deterministic
`attempts/rejected_<first-32-lowercase-hex-digits>/` directory before writing
fresh active bytes. The 128-bit directory prefix stays within the legacy
Windows path budget; `rejection.json` retains the complete 64-hex freeze hash,
and any existing prefix target fails closed as a collision.
### Current compiler diagnostic roundtrip

`tools/traffic_v2/run_diagnostic_compile_roundtrip.py` creates a new external
test repository and explicitly diagnostic registry/freeze. It binds the actual
current compiler and pinned runtime, generates labelled synthetic road/view
fixtures, and runs the real compiler, staged semantic validator and five-artifact
byte rebuild. `--acquire-naip` acquires the exact public metadata/license inputs;
`--reuse-naip-freeze` reuses and revalidates already captured real metadata.
The historical source, product and Lead camera pins remain unchanged.

`tools/traffic_v2/verify_diagnostic_candidate_protocol.py` invokes the exact
gate-generated registered verifier argv in that external test repository and
feeds the real output into `validate_d_result_semantics`. Empty and single-road
fixtures must pass; conflicting lane evidence must remain rejected. Machine
witnesses live only in the external test repository. These synthetic fixtures
and images cannot establish shipping traffic source acceptance or authentic
Lead full-resolution inspection; no human inspection record is generated.
