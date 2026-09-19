# Training asset provenance

The six lesson `.asset` files, `VoiceScripts.json`, and every `Voice/*.wav` file are original QuestFlight content authored for the approved Flight Training V1 milestone, September 18, 2026. There are no downloaded recordings, aircraft models, photographs, or third-party curriculum packages in this directory.

Each WAV is generated from the matching `id` and original `text` in `VoiceScripts.json` by `scripts/generate_training_voice.ps1`, using the installed Windows Speech API and Microsoft David Desktop voice. The speech engine and voice software are not included in the application. These original scripts and generated recordings are project-owned content distributed with the owner's authorized QuestFlight builds; no separate public-domain or third-party recording-license claim is made.

Unity imports the recordings as mono 22.05 kHz Vorbis, compressed in memory. Only one narration source plays at a time. WAV sources are retained to reproduce the import; their `.meta` GUIDs must be preserved.

Instructional references, paraphrased into short original cues:

- FAA Airplane Flying Handbook, ground operations, basic maneuvers, takeoff, traffic patterns, and approach/landing: https://www.faa.gov/regulations_policies/handbooks_manuals/aviation/airplane_handbook
- Cessna 172P POH, Section 4 (manufacturer document hosted by BEFA): https://www.befa.org/wp-content/uploads/2019/12/POH-Cessna-172P.pdf
- City of Boulder published airport pattern: https://bouldercolorado.gov/services/airport-traffic-pattern

Aircraft reference anchors are approximately 55 KIAS for rotation, 70–80 KIAS for normal climb-out and 60–70 KIAS for a flaps-down approach. Runtime scoring observes the existing native calibrated-airspeed channel with forgiving simulator exercise bands; it does not claim a validated IAS/CAS calibration. Heading targets use the existing true-heading indication, distinct from the magnetic compass. Local pattern altitude is 6300 feet MSL. The reservoir scenic demo is not treated as an official pattern.

Dwell times, deviation tolerances, a 300-foot approach warning gate, touchdown limits, and control thresholds are simulator exercise policy, not POH limitations or proficiency standards. The curriculum does not verify unmodeled doors, paperwork, radio clearance, or a complete preflight. It supplies no training credit.
