using System;
using QuestFlight.Product;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Analog;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Single deterministic Editor-only integration point for cockpit V3.
    /// Runtime code never discovers, repairs, or constructs product hierarchy.
    /// </summary>
    public static class CockpitCompletionV3Authoring
    {
        public const string DynamicInstrumentObjectName =
            CockpitAnalogInstrumentAuthoring.DynamicRendererObjectName;
        public const string AvionicsObjectName =
            "ProductionInstrumentDisplayReadouts";
        public const string AvionicsHardwareObjectName =
            "ProductionStaticDisplayHardware";
        public const string InstrumentLensObjectPath =
            "AircraftVisualRoot/ProductionPrincipalInstrumentLensArray";

        [MenuItem("QuestFlight/Cockpit/Apply Cockpit Completion V3")]
        public static void ApplyAssetsAndPrefab()
        {
            CockpitInstrumentV2Authoring.BuildAssetsOnly();
            CockpitAvionicsV2PresentationAuthoring.BuildAssetsOnly();

            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                CockpitDynamicsIntegrationAuthoring.AuthorPrefabContents(root);
                IntegrateInstrumentBatch(root);
                RestoreFlushInstrumentLensTransform(root);
                IntegrateAvionicsBatch(root);
                if (!ValidatePrefabContents(root, out string error))
                    throw new InvalidOperationException(error);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath,
                    out bool success);
                Require(success && saved != null,
                    "Unity failed to save the authored cockpit V3 prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateProductPrefab();
            Debug.Log(
                "[QuestFlight][CockpitCompletionV3] Serialized represented " +
                "controls, one InstrumentV2 renderer, and one AvionicsV2 renderer.");
        }

        [MenuItem("QuestFlight/Cockpit/Validate Cockpit Completion V3")]
        public static void ValidateProductPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                if (!ValidatePrefabContents(root, out string error))
                    throw new InvalidOperationException(error);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static bool ValidatePrefabContents(
            GameObject root,
            out string error)
        {
            try
            {
                Require(root != null, "Aircraft prefab contents are required.");
                Require(
                    CockpitDynamicsIntegrationAuthoring
                        .ValidateAuthoredPrefabContents(root, out string dynamicsError),
                    dynamicsError);

                CockpitInstrumentV2BatchController[] instruments =
                    root.GetComponentsInChildren<
                        CockpitInstrumentV2BatchController>(true);
                Require(instruments.Length == 1,
                    $"Expected one InstrumentV2 batch controller; found " +
                    $"{instruments.Length}.");
                Require(
                    instruments[0].ValidateAuthoredReferences(
                        out string instrumentError),
                    instrumentError);

                CockpitAvionicsV2BatchController[] avionics =
                    root.GetComponentsInChildren<
                        CockpitAvionicsV2BatchController>(true);
                Require(avionics.Length == 1,
                    $"Expected one AvionicsV2 batch controller; found " +
                    $"{avionics.Length}.");
                Require(
                    avionics[0].ValidateAuthoredReferences(
                        out string avionicsError),
                    avionicsError);

                Require(
                    root.GetComponentsInChildren<
                        ProductionAnalogInstrumentController>(true).Length == 0,
                    "The legacy analog controller must not coexist with InstrumentV2.");
                RequireRendererContract(
                    FindUniqueTransform(root, DynamicInstrumentObjectName),
                    "InstrumentV2");
                RequireRendererContract(
                    FindUniqueTransform(root, AvionicsObjectName),
                    "AvionicsV2");
                Require(
                    CockpitDepthOwnershipEditorContract.ValidatePrefabContents(
                        root, out string depthOwnershipError),
                    depthOwnershipError);

                Transform lens = root.transform.Find(InstrumentLensObjectPath);
                Require(lens != null,
                    "The measured principal-instrument lens array is missing.");
                Require(lens.localPosition.sqrMagnitude < 0.0000000001f &&
                        Quaternion.Angle(lens.localRotation, Quaternion.identity) < 0.001f &&
                        Vector3.Distance(lens.localScale, Vector3.one) < 0.000001f,
                    "Principal-instrument glass must use its measured vertex positions " +
                    "without a transform-space offset or scale correction.");
                Mesh lensMesh = lens.GetComponent<MeshFilter>()?.sharedMesh;
                Require(lensMesh != null && lensMesh.vertexCount == 13 * 25 &&
                        lensMesh.triangles.Length / 3 == 13 * 24,
                    "Principal-instrument glass must contain exactly 13 measured " +
                    "24-segment aperture lenses in one mesh.");

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void IntegrateInstrumentBatch(GameObject root)
        {
            CockpitInstrumentV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitInstrumentV2Layout>(
                    CockpitInstrumentV2Authoring.LayoutPath);
            Require(layout != null, "InstrumentV2 layout is missing.");
            Require(layout.Validate(out string layoutError), layoutError);

            Transform meter = root.transform.Find(
                CockpitAnalogInstrumentAuthoring.MeterRendererPath);
            Require(meter != null,
                "The frozen Meter_01 renderer path is missing.");
            MeshFilter meterFilter = meter.GetComponent<MeshFilter>();
            MeshRenderer meterRenderer = meter.GetComponent<MeshRenderer>();
            Require(meterFilter != null && meterRenderer != null,
                "The Meter_01 renderer is incomplete.");
            meterFilter.sharedMesh = layout.NeutralizedSourceMesh;
            // The batched material selects the frozen source atlas for the
            // neutralized base mesh and the project-authored atlas only for
            // overlay vertices via TEXCOORD3. Both renderers must therefore
            // share this one material.
            meterRenderer.sharedMaterials =
                new[] { layout.ExistingSourceMaterial };

            Transform dynamicTransform =
                FindUniqueTransform(root, DynamicInstrumentObjectName);
            MeshFilter dynamicFilter =
                dynamicTransform.GetComponent<MeshFilter>();
            MeshRenderer dynamicRenderer =
                dynamicTransform.GetComponent<MeshRenderer>();
            Require(dynamicFilter != null && dynamicRenderer != null,
                "The existing dynamic-instrument batch object is incomplete.");
            ConfigureOneDrawRenderer(
                dynamicFilter,
                dynamicRenderer,
                layout.CombinedDynamicMesh,
                layout.ExistingSourceMaterial);

            ProductionAnalogInstrumentController[] legacy =
                root.GetComponentsInChildren<
                    ProductionAnalogInstrumentController>(true);
            for (int index = 0; index < legacy.Length; index++)
                UnityEngine.Object.DestroyImmediate(legacy[index]);

            Transform host = root.transform.Find(
                CockpitDynamicsIntegrationAuthoring.AnimatedControlsPath);
            Require(host != null, "The cockpit presentation host is missing.");
            CockpitInstrumentV2BatchController controller =
                RequireOrAddSingle<CockpitInstrumentV2BatchController>(
                    root, host.gameObject);
            controller.ConfigureAuthoredReferences(
                RequireSingleAuthority(root),
                layout,
                meterFilter,
                meterRenderer,
                dynamicFilter,
                dynamicRenderer);
            EditorUtility.SetDirty(controller);
        }

        private static void IntegrateAvionicsBatch(GameObject root)
        {
            CockpitAvionicsV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitAvionicsV2Layout>(
                    CockpitAvionicsV2PresentationAuthoring.LayoutPath);
            Require(layout != null, "AvionicsV2 layout is missing.");
            Require(layout.Validate(out string layoutError), layoutError);

            MeshRenderer sourceRenderer =
                CockpitDepthOwnershipEditorContract
                    .ResolveSourceDisplayRenderer(root);
            MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
            Mesh neutralizedSource = CockpitAvionicsV2PresentationAuthoring
                .BuildNeutralizedSourceMeshAsset(sourceFilter.sharedMesh);

            Transform hardware = sourceRenderer.transform.parent.Find(
                AvionicsHardwareObjectName);
            if (hardware == null)
            {
                var hardwareObject = new GameObject(AvionicsHardwareObjectName);
                hardware = hardwareObject.transform;
                hardware.SetParent(sourceRenderer.transform.parent, false);
                hardware.localPosition = sourceRenderer.transform.localPosition;
                hardware.localRotation = sourceRenderer.transform.localRotation;
                hardware.localScale = sourceRenderer.transform.localScale;
                hardwareObject.AddComponent<MeshFilter>();
                hardwareObject.AddComponent<MeshRenderer>();
            }
            MeshFilter hardwareFilter = hardware.GetComponent<MeshFilter>();
            MeshRenderer hardwareRenderer = hardware.GetComponent<MeshRenderer>();
            Require(hardwareFilter != null && hardwareRenderer != null,
                "The neutralized static-display hardware renderer is incomplete.");
            hardwareFilter.sharedMesh = neutralizedSource;
            EditorUtility.CopySerialized(sourceRenderer, hardwareRenderer);
            hardwareRenderer.enabled = true;
            EditorUtility.SetDirty(hardwareFilter);
            EditorUtility.SetDirty(hardwareRenderer);
            ReplaceRendererReference(
                root.GetComponentInChildren<AircraftVisibilityProfileController>(true),
                "interiorRenderers", sourceRenderer, hardwareRenderer,
                "aircraft interior visibility");
            ReplaceRendererReference(
                root.GetComponentInChildren<ProductFlightModeMenuView>(true),
                "aircraftPresentationRenderers", sourceRenderer, hardwareRenderer,
                "startup menu suppression");
            sourceRenderer.enabled = false;
            EditorUtility.SetDirty(sourceRenderer);

            Transform display = FindUniqueTransform(root, AvionicsObjectName);
            MeshFilter filter = display.GetComponent<MeshFilter>();
            MeshRenderer renderer = display.GetComponent<MeshRenderer>();
            Require(filter != null && renderer != null,
                "The represented avionics readout batch is incomplete.");
            ConfigureOneDrawRenderer(
                filter,
                renderer,
                layout.CombinedGlyphMesh,
                layout.ExistingReadoutMaterial);

            Transform host = root.transform.Find(
                CockpitDynamicsIntegrationAuthoring.AnimatedControlsPath);
            Require(host != null, "The cockpit presentation host is missing.");
            CockpitAvionicsV2BatchController controller =
                RequireOrAddSingle<CockpitAvionicsV2BatchController>(
                    root, host.gameObject);
            controller.ConfigureAuthoredReferences(
                RequireSingleAuthority(root),
                layout,
                filter,
                renderer);
            EditorUtility.SetDirty(controller);
        }

        private static void RestoreFlushInstrumentLensTransform(GameObject root)
        {
            Transform lens = root.transform.Find(InstrumentLensObjectPath);
            Require(lens != null,
                "The accepted principal-instrument lens array is missing.");
            Mesh mesh = lens.GetComponent<MeshFilter>()?.sharedMesh;
            Require(mesh != null && mesh.normals.Length > 0,
                "The accepted principal-instrument lens mesh is incomplete.");
            // Lens vertices are authored directly from the measured source
            // enclosure plane.  A transform-space depth correction is unsafe
            // here because Meter_01 carries an imported 11.35049 scale and
            // turns a small-looking local offset into visibly floating glass.
            lens.localPosition = Vector3.zero;
            lens.localRotation = Quaternion.identity;
            lens.localScale = Vector3.one;
            EditorUtility.SetDirty(lens);
        }

        private static MonoBehaviour RequireSingleAuthority(GameObject root)
        {
            ProductionCockpitSystemsController[] values =
                root.GetComponentsInChildren<
                    ProductionCockpitSystemsController>(true);
            Require(values.Length == 1,
                $"Expected one cockpit systems authority; found {values.Length}.");
            return values[0];
        }

        private static void ConfigureOneDrawRenderer(
            MeshFilter filter,
            MeshRenderer renderer,
            Mesh mesh,
            Material material)
        {
            Require(mesh != null && material != null,
                "A batched cockpit mesh/material is missing.");
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { material };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.BlendProbesAndSkybox;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.additionalVertexStreams = null;
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);
        }

        private static void ReplaceRendererReference(
            UnityEngine.Object owner,
            string propertyName,
            Renderer source,
            Renderer replacement,
            string label)
        {
            Require(owner != null, $"The {label} authority is missing.");
            var serialized = new SerializedObject(owner);
            SerializedProperty renderers = serialized.FindProperty(propertyName);
            Require(renderers != null && renderers.isArray,
                $"The {label} renderer array is unavailable.");
            int replacements = 0;
            int replacementReferences = 0;
            for (int index = 0; index < renderers.arraySize; index++)
            {
                SerializedProperty element = renderers.GetArrayElementAtIndex(index);
                if (element.objectReferenceValue == source)
                {
                    element.objectReferenceValue = replacement;
                    replacements++;
                }
                if (element.objectReferenceValue == replacement)
                    replacementReferences++;
            }
            Require(replacements <= 1 && replacementReferences == 1,
                $"Expected one idempotent {label} replacement binding; found " +
                $"source={replacements}, replacement={replacementReferences}.");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner);
        }

        private static void RequireRendererContract(
            Transform value,
            string label)
        {
            MeshFilter filter = value.GetComponent<MeshFilter>();
            MeshRenderer renderer = value.GetComponent<MeshRenderer>();
            Require(filter != null && renderer != null,
                $"{label} renderer is incomplete.");
            Require(
                renderer.sharedMaterials.Length == 1 &&
                renderer.sharedMaterial != null,
                $"{label} must use one material.");
            Require(renderer.shadowCastingMode == ShadowCastingMode.Off &&
                    !renderer.receiveShadows,
                $"{label} must not add shadow cost.");
        }

        private static Transform FindUniqueTransform(
            GameObject root,
            string name)
        {
            Transform found = null;
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform candidate = transforms[index];
                if (!string.Equals(
                        candidate.name, name, StringComparison.Ordinal))
                    continue;
                Require(found == null,
                    $"Multiple authored transforms are named '{name}'.");
                found = candidate;
            }
            Require(found != null,
                $"Required authored transform '{name}' is missing.");
            return found;
        }

        private static T RequireOrAddSingle<T>(
            GameObject root,
            GameObject host)
            where T : Component
        {
            T[] values = root.GetComponentsInChildren<T>(true);
            Require(values.Length <= 1,
                $"Multiple {typeof(T).Name} components exist.");
            return values.Length == 1
                ? values[0]
                : host.AddComponent<T>();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(message)
                        ? "Cockpit V3 authoring contract failed."
                        : message);
        }
    }
}
