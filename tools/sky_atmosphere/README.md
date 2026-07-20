# Sky and atmosphere asset pipeline

This pipeline converts the externally cached, official Poly Haven 4K Radiance HDR into the committed 2K linear-radiance derivative. Raw source files and visual-evaluation previews remain under the milestone artifact root and outside Git.

Install the pinned dependency range with `python -m pip install -r tools/sky_atmosphere/requirements.txt` when NumPy is not already available.

```powershell
python tools/sky_atmosphere/build_hdri_derivative.py `
  --input <artifact-root>\workstream_a\raw_source\kloppenheim_03_puresky_4k.hdr `
  --output QuestFlight\Assets\Production\Environment\Sky\Kloppenheim03_PureSky_2K.hdr `
  --downsample-factor 2 `
  --expected-source-sha256 f906a7334e5d57007fa5516bd141df2ada8fdc3d2ba0c8bdf445d64f1bbb010a `
  --report <artifact-root>\workstream_a\sky_derivative_report.json
```

Then run Unity's `QuestFlightLab.EditorTools.PhotorealSkyAssetAuthoring.ConfigureBatch` method. It configures cylindrical cubemap conversion, mipmaps, a 1024-pixel Android cap, ASTC HDR 6×6, and the `Skybox/Cubemap` material. It never edits a scene or prefab. The derivative report separately measures source wrap continuity; final cubemap-seam acceptance still requires stereo captures.

Integration into `ProductionVerticalSlice` is intentionally owned by the lead agent. See `docs/MOUNTAIN_HORIZON_V2_DECISION.md` for the lighting and horizon contract.
