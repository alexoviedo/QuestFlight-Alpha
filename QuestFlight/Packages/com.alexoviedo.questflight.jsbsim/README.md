# QuestFlight JSBSim native package

This embedded package contains the pinned, replaceable native JSBSim shared
libraries, a patched and explicitly pinned C172P diagnostic model, an allocation-
free managed C ABI owner, build-time StreamingAssets injection, and runtime
payload hash validation.

The package is not a certification or fidelity claim. Native JSBSim is the
only product flight authority in Editor, development, and release builds. The
coordinator fails closed on native preparation, model integrity, ABI,
initialization, reset, non-finite-state, or step failure; it cannot select a
Unity flight-dynamics fallback.

See `Documentation~/SOURCE_AND_LICENSE.md` and `SOURCE_MANIFEST.json` for the
exact upstream revision, binary hashes, build flags, and license details.
The C172P derivative and its private-use distribution limitation are documented
in `Documentation~/C172P_MODEL_CONFIGURATION.md`. Sole authority does not imply
certified handling fidelity: condition-specific scenario envelopes and exact-APK
Quest handling/performance evidence remain open.
