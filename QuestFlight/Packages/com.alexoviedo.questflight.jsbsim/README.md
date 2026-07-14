# QuestFlight JSBSim native package

This embedded package contains the pinned, replaceable native JSBSim shared
libraries, the minimal C172 runtime data needed by QuestFlight, an allocation-
free managed C ABI owner, and build-time StreamingAssets injection.

The package is not a certification or fidelity claim. Product release builds
remain on the Unity prototype backend. The native backend may be selected only
in the Unity Editor or a `DEVELOPMENT_BUILD`, and the product coordinator falls
back to the prototype if native initialization or stepping fails.

See `Documentation~/SOURCE_AND_LICENSE.md` and `SOURCE_MANIFEST.json` for the
exact upstream revision, binary hashes, build flags, and license details.
