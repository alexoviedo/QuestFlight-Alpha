# QuestFlight JSBSim native package

This embedded package contains the pinned, replaceable native JSBSim shared
libraries, a patched and explicitly pinned C172P diagnostic model, an allocation-
free managed C ABI owner, build-time StreamingAssets injection, and runtime
payload hash validation.

The package is not a certification or fidelity claim. Product release builds
remain on the Unity prototype backend. The native backend may be selected only
in the Unity Editor or a `DEVELOPMENT_BUILD`, and the product coordinator falls
back to the prototype if native initialization or stepping fails.

See `Documentation~/SOURCE_AND_LICENSE.md` and `SOURCE_MANIFEST.json` for the
exact upstream revision, binary hashes, build flags, and license details.
The C172P derivative and its unresolved distribution limitation are documented
in `Documentation~/C172P_MODEL_CONFIGURATION.md`. The package is not eligible
for native-backend promotion while that limitation or the required scenario and
Quest handling gates remain open.
