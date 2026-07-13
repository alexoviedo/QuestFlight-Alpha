# Validation

Run from the repository root:

```powershell
.\scripts\validate_product.ps1
.\scripts\run_unity_tests.ps1 -TestPlatform EditMode
.\scripts\run_unity_tests.ps1 -TestPlatform PlayMode
.\scripts\run_production_visual_qa.ps1
.\scripts\build_production_quest.ps1
```

Machine gates prove dependency, serialization, reference, budget, image-integrity, and build contracts. They do not prove stereo temporal stability, comfort, visual quality, handling fidelity, or sustained Quest frame timing. Those claims require the three-launch worn gate and the 90-second warmup plus 60-second measurement.
