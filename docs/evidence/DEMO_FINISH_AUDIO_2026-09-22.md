# Demo finish: aircraft audio

## Bounded changes

- `ProductionC172AudioController` applies the Aircraft volume preference to engine, wind, ground roll, flap motor, starter, switch and touchdown sounds. The stall warning remains independent of effects volume and speech ducking.
- The serialized cockpit and airframe one-shot sources were at volume zero, and the runtime never restored them. Their previously authored cues now have an explicit gain.
- The 18-second start recording is gated to actual starter engagement and fades over 160 ms once the starter disengages or the engine catches. It cannot continue as a second running-engine soundtrack or stack on repeated starts.
- Starter playback uses the existing airframe event source; switch and touchdown one-shots share the separate cockpit event source. Finishing the starter fade therefore cannot stop a switch click or landing impact. Pausing freezes an active starter recording; switch/touchdown edges are not queued while paused.
- Volume changes, runway rumble and flap-motor transitions are smoothed. Touchdown strength reads the last airborne descent rate, rather than a contact sample that may already have been stopped or bounced by the ground reaction. Flight dynamics are unchanged.
- New-flight simulation-time reset clears stale cue state.

## Source and loop repair

The existing recording is [JLWSound's Cessna 172E interior recording](https://freesound.org/people/JLWSound/sounds/322321/), published under CC0. The page exposes a public [high-quality MP3 preview](https://cdn.freesound.org/previews/322/322321_2489703-hq.mp3). It is still a compressed preview, not the original lossless master.

The existing low-quality preview decodes at 24 kHz; the new preview decodes at 48 kHz. Both contain the same 1,314.206-second recording. Matching excerpts, downsampled for comparison, correlate from 0.99928 to 0.99977. The source intervals remain start 447–465 s, idle 490–502 s, propeller 820–830 s, and cruise 880–892 s. In particular, the previously rejected contaminated idle interval was not restored.

The previous offline loop crossfade replaced the tail with the beginning but kept the duplicated overlap. At playback wrap it jumped backwards by 0.75 s. The corrected overlap is included once, leaving adjacent original samples across the wrap. Deterministic wind and ground-roll noise now use the same continuous construction.

| Loop | Old boundary jump, full scale | Corrected boundary jump | Corrected transition percentile among ordinary samples |
|---|---:|---:|---:|
| Idle | 0.24319 | 0.00278 | 58.7% |
| Cruise | 0.07190 | 0.00699 | 64.4% |
| Propeller | 0.32162 | 0.00220 | 33.6% |
| Ground roll | 0.04919 | 0.00168 | 22.6% |

All four original boundary jumps exceeded every ordinary sample transition in their respective clips. These measurements demonstrate the removed discontinuities; they do not substitute for a listening judgment about sound quality.

Seven WAVs changed. Flap motor, warning, switch and touchdown WAVs are byte-identical. Existing engine/noise RMS levels are retained, with differences below 0.01 dB; runtime source count and import settings are unchanged. Shorter loops slightly reduce decoded sample counts. Source hashes, output hashes and processing notes are recorded in `Assets/Production/Audio/C172/Clips/SOURCE_MANIFEST.json`. All eleven output hashes were checked after promotion. Unity GUIDs are unchanged.

Reproduce with `tools/audio/prepare_c172_audio.py`, the high-quality source supplied via `--input`, its URL via `--source-url`, and the previous clips directory via `--reference-clips`. Raw downloads and audition files remain outside the repository.

## Instructor review

All 106 Michael clips were measured. Every manifest duration agrees with its WAV. No clip ends with a truncated active waveform; median trailing silence is 0.577 s. Integrated loudness ranges from -29.65 to -27.31 LUFS, a 2.34 LU spread. There was no objective case for bulk regeneration or aggressive compression, so the accepted Michael recordings and their narration scheduling remain unchanged.

An automatic speech scan of the four revised engine excerpts returned no detected words. As with the previous idle correction, this is screening evidence, not proof of inaudibility. Human listening acceptance and runtime starter/landing balance remain pending the integrated headset check.

## Review artifacts

External folder: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/demo_finish_20260922/audio/`.

- `listen.html`: previous/revised engine and auxiliary audition.
- `polish_metrics.json`: source-content correlation, seam measurements and RMS matching.
- `engine_speech_audit.json`: automated speech screening and its limits.
- `voice_audit.json`: per-clip voice loudness, duration and tail measurements.

No Unity build or headset performance acceptance is claimed by this audio-only evidence. The root task performs integrated build and visual validation.

The existing `ProductionC172AudioEditModeTests` fixture now includes a focused regression check of the actual runtime AudioSource gains: initially silent event sources become audible, the aircraft-volume setting silences ordinary effects, and the stall warning stays audible. It uses an inactive presentation probe, without starting a flight backend. Test execution is delegated to the integrated validation run.
