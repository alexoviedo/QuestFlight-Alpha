using System;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using QuestFlight.Product;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor
{
    /// <summary>Authors the fixed, presentation-only Flight Modes showroom.</summary>
    public static class ProductStartupShowroomAuthoring
    {
        public const string PresentationRootName = "FlightModesSelectorPanel";
        public const string ShowroomContentName = "StartupShowroomContent";
        public const string SelectionContentName = "StartupSelectionContent";
        public const string TransitionContentName = "StartupTransitionContent";
        public const string StateContractName = "StartupModeStateContract";
        public const string BackdropName = "StartupShowroomBackdrop";
        public const string ShellName = "StartupShowroomShell";
        public const string ShellMaterialPath =
            "Assets/Production/Materials/StartupShowroomShell.mat";
        public const string SurfaceMaterialPath =
            "Assets/Production/Materials/StartupShowroomSurface.mat";
        public const string AccentMaterialPath =
            "Assets/Production/Materials/StartupShowroomAccent.mat";
        public const string OverlayMaterialPath =
            "Assets/Production/Materials/StartupShowroomOverlay.mat";
        public const string BackdropMaterialPath =
            "Assets/Production/Materials/StartupShowroomBackdrop.mat";
        public const string TextMaterialPath =
            "Assets/Production/Materials/StartupShowroomText.mat";
        public const string BackdropTexturePath =
            "Assets/Production/UI/StartupShowroomBackdrop.png";
        public const string ShaderPath =
            "Assets/Production/Materials/StartupShowroomUnlit.shader";
        public const string FadeShaderPath =
            "Assets/Production/Materials/StartupShowroomFade.shader";
        public const string FeedbackClipPath =
            "Assets/Production/Audio/C172/Clips/C172_Switch_Click.wav";

        private static readonly Color Navy =
            new Color(0.018f, 0.032f, 0.056f, 1f);
        private static readonly Color NavyTop =
            new Color(0.035f, 0.115f, 0.17f, 1f);
        private static readonly Color Card =
            new Color(0.045f, 0.07f, 0.105f, 0.97f);
        private static readonly Color CardHover =
            new Color(0.07f, 0.22f, 0.31f, 1f);
        private static readonly Color Amber =
            new Color(1f, 0.51f, 0.12f, 1f);
        private static readonly Color Cyan =
            new Color(0.20f, 0.78f, 1f, 1f);
        private static readonly Color White =
            new Color(0.92f, 0.96f, 1f, 1f);
        private static readonly Color Muted =
            new Color(0.52f, 0.62f, 0.70f, 1f);

        public static void EnsureMaterialAssets()
        {
            ConfigureBackdropTexture();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Require(shader != null,
                "The project-authored startup showroom shader is missing.");
            Shader fadeShader = AssetDatabase.LoadAssetAtPath<Shader>(FadeShaderPath);
            Require(fadeShader != null,
                "The project-authored startup showroom fade shader is missing.");
            EnsureMaterial(
                ShellMaterialPath,
                shader,
                Navy,
                NavyTop,
                1f,
                CullMode.Front,
                CompareFunction.LessEqual,
                true,
                1f,
                1990,
                null);
            EnsureMaterial(
                SurfaceMaterialPath,
                shader,
                Card,
                Card,
                0f,
                CullMode.Back,
                CompareFunction.LessEqual,
                true,
                1f,
                2010,
                null);
            EnsureMaterial(
                AccentMaterialPath,
                shader,
                Cyan,
                Cyan,
                0f,
                CullMode.Back,
                CompareFunction.LessEqual,
                true,
                1f,
                2011,
                null);
            EnsureMaterial(
                OverlayMaterialPath,
                fadeShader,
                Color.black,
                Color.black,
                0f,
                CullMode.Front,
                CompareFunction.Always,
                false,
                0f,
                4000,
                null);
            EnsureMaterial(
                BackdropMaterialPath,
                shader,
                Color.white,
                Color.white,
                0f,
                CullMode.Off,
                CompareFunction.LessEqual,
                true,
                1f,
                2000,
                RequireAsset<Texture2D>(BackdropTexturePath));
        }

        public static ProductFlightModeMenuView AuthorAircraftPresentation(
            GameObject aircraftRoot,
            Transform calibrationRoot)
        {
            ProductFlightModeMenuView[] existingViews = calibrationRoot
                .GetComponentsInChildren<ProductFlightModeMenuView>(true);
            Require(existingViews.Length <= 1,
                $"Refusing duplicate startup showroom views; found {existingViews.Length}.");
            if (existingViews.Length == 1)
            {
                ProductFlightModeMenuView existing = existingViews[0];
                if (existing.LayoutVersion == ProductFlightModeMenuView.CurrentLayoutVersion)
                {
                    Require(existing.transform.parent == calibrationRoot,
                        "The startup showroom must be directly under UserViewCalibrationOffset.");
                    Require(existing.name == PresentationRootName,
                        "The startup showroom root identity changed unexpectedly.");
                    Require(existing.ValidateAuthoredReferences(out string error), error);
                    ValidatePresentation(existing.gameObject);
                    existing.gameObject.SetActive(false);
                    return existing;
                }
                Object.DestroyImmediate(existing.gameObject);
            }

            Transform prior = calibrationRoot.Find(PresentationRootName);
            if (prior != null)
                Object.DestroyImmediate(prior.gameObject);

            ProductionSeatCalibrationController seat = aircraftRoot
                .GetComponentsInChildren<ProductionSeatCalibrationController>(true)
                .Single();
            ProductionCockpitSystemsController systems = aircraftRoot
                .GetComponentsInChildren<ProductionCockpitSystemsController>(true)
                .Single();
            OvrPointerSourceRegistry registry = aircraftRoot
                .GetComponentsInChildren<OvrPointerSourceRegistry>(true)
                .Single();
            TextMesh textTemplate = seat.AuthoredPanelText;
            Require(textTemplate != null,
                "The accepted seat-calibration TextMesh template is missing.");
            EnsureTextMaterial(textTemplate);

            Material surface = RequireAsset<Material>(SurfaceMaterialPath);
            Material accent = RequireAsset<Material>(AccentMaterialPath);
            Material overlay = RequireAsset<Material>(OverlayMaterialPath);
            Material backdrop = RequireAsset<Material>(BackdropMaterialPath);
            AudioClip feedbackClip = RequireAsset<AudioClip>(FeedbackClipPath);

            var presentation = new GameObject(PresentationRootName);
            presentation.transform.SetParent(calibrationRoot, false);
            presentation.transform.SetAsFirstSibling();
            ProductFlightModeMenuView view =
                presentation.AddComponent<ProductFlightModeMenuView>();

            var showroomContent = new GameObject(ShowroomContentName);
            showroomContent.transform.SetParent(presentation.transform, false);
            CreateSphere(
                ShellName,
                showroomContent.transform,
                Vector3.zero,
                20f,
                RequireAsset<Material>(ShellMaterialPath));
            CreateQuad(
                BackdropName,
                showroomContent.transform,
                new Vector3(0f, 0.02f, 1.65f),
                new Vector2(5.8f, 3.2625f),
                backdrop,
                removeCollider: true);

            AudioSource audio = presentation.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.clip = feedbackClip;

            var selection = new GameObject(SelectionContentName);
            selection.transform.SetParent(showroomContent.transform, false);
            CreateBox(
                "MenuBackplate",
                selection.transform,
                new Vector3(-0.47f, 0.04f, 1.47f),
                new Vector3(1.48f, 1.28f, 0.025f),
                surface,
                removeCollider: true);
            CreateBox(
                "MenuAccentRule",
                selection.transform,
                new Vector3(-1.185f, 0.31f, 1.445f),
                new Vector3(0.012f, 0.51f, 0.012f),
                accent,
                removeCollider: true);

            CreateText(
                "QuestFlightTitle",
                selection.transform,
                textTemplate,
                "QUESTFLIGHT",
                new Vector3(-1.10f, 0.59f, 1.44f),
                0.0175f,
                76,
                White);
            CreateText(
                "AircraftSubtitle",
                selection.transform,
                textTemplate,
                "C172  ·  KBDU  ·  FLIGHT OPERATIONS",
                new Vector3(-1.09f, 0.44f, 1.44f),
                0.0065f,
                52,
                Cyan);

            ProductFlightModeMenuButton demoButton = CreateMenuButton(
                selection.transform,
                textTemplate,
                view,
                "DemoFlightCard",
                "A   DEMO FLIGHT",
                "GUIDED KBDU DEPARTURE · SHARED-CONTROL HANDOFF",
                new Vector3(-0.47f, 0.23f, 1.43f),
                ProductFlightModeMenuButton.MenuAction.Demo,
                surface,
                seat,
                registry,
                audio,
                Card,
                CardHover,
                Amber);
            ProductFlightModeMenuButton freeFlightButton = CreateMenuButton(
                selection.transform,
                textTemplate,
                view,
                "FreeFlightCard",
                "B   FREE FLIGHT",
                "ENGINE RUNNING · FULLY MANUAL C172",
                new Vector3(-0.47f, -0.01f, 1.43f),
                ProductFlightModeMenuButton.MenuAction.FreeFlight,
                surface,
                seat,
                registry,
                audio,
                Card,
                CardHover,
                Amber);
            ProductFlightModeMenuButton lessonsButton = CreateMenuButton(
                selection.transform,
                textTemplate,
                view,
                "LessonsCard",
                "X   WEATHER",
                "CALM",
                new Vector3(-0.47f, -0.25f, 1.43f),
                ProductFlightModeMenuButton.MenuAction.Weather,
                surface,
                seat,
                registry,
                audio,
                new Color(0.04f, 0.05f, 0.07f, 0.94f),
                new Color(0.13f, 0.105f, 0.06f, 1f),
                Amber);
            ProductFlightModeMenuButton calibrationButton = CreateMenuButton(
                selection.transform,
                textTemplate,
                view,
                "SeatCalibrationCard",
                "MENU   SEAT CALIBRATION",
                "",
                new Vector3(-0.47f, -0.47f, 1.43f),
                ProductFlightModeMenuButton.MenuAction.SeatCalibration,
                surface,
                seat,
                registry,
                audio,
                new Color(0.035f, 0.055f, 0.08f, 0.94f),
                CardHover,
                Cyan,
                new Vector3(1.26f, 0.12f, 0.035f));

            TextMesh status = CreateText(
                "StartupStatus",
                selection.transform,
                textTemplate,
                "INITIALIZING NATIVE FLIGHT SYSTEMS",
                new Vector3(-1.09f, -0.61f, 1.44f),
                0.0052f,
                50,
                Cyan);
            CreateText(
                "StartupNavigationHint",
                selection.transform,
                textTemplate,
                "POINT + TRIGGER  ·  STICK ↑↓ + CLICK",
                new Vector3(-1.09f, -0.70f, 1.44f),
                0.0041f,
                44,
                Muted);

            var transition = new GameObject(TransitionContentName);
            transition.transform.SetParent(showroomContent.transform, false);
            CreateBox(
                "TransitionBackplate",
                transition.transform,
                new Vector3(0f, 0.02f, 1.38f),
                new Vector3(1.65f, 0.34f, 0.025f),
                surface,
                removeCollider: true);
            TextMesh transitionText = CreateText(
                "TransitionTitle",
                transition.transform,
                textTemplate,
                "ENTERING COCKPIT",
                new Vector3(0f, 0.08f, 1.35f),
                0.011f,
                64,
                White,
                TextAnchor.MiddleCenter,
                TextAlignment.Center);
            CreateText(
                "TransitionStatus",
                transition.transform,
                textTemplate,
                "VERIFYING COCKPIT SYSTEMS",
                new Vector3(0f, -0.07f, 1.35f),
                0.0052f,
                48,
                Cyan,
                TextAnchor.MiddleCenter,
                TextAlignment.Center);
            transition.SetActive(false);

            GameObject stateContractObject = new GameObject(StateContractName);
            stateContractObject.transform.SetParent(presentation.transform, false);
            TextMesh stateContract = stateContractObject.AddComponent<TextMesh>();
            CopyTextStyle(textTemplate, stateContract);
            stateContract.text = ProductFlightModeCoordinator.SelectionText;
            stateContractObject.GetComponent<MeshRenderer>().enabled = false;

            GameObject fadeOverlay = CreateBox(
                "StartupComfortFade",
                presentation.transform,
                Vector3.zero,
                new Vector3(0.6f, 0.6f, 0.6f),
                overlay,
                removeCollider: true);
            Renderer fadeRenderer = fadeOverlay.GetComponent<Renderer>();

            RayInteractorRayVisual[] rayVisuals = aircraftRoot
                .GetComponentsInChildren<RayInteractorRayVisual>(true);
            RayInteractorCursorVisual[] cursorVisuals = aircraftRoot
                .GetComponentsInChildren<RayInteractorCursorVisual>(true);
            Require(rayVisuals.Length == 4,
                $"Expected four accepted Interaction SDK ray visuals, found {rayVisuals.Length}.");
            Require(cursorVisuals.Length == 4,
                $"Expected four accepted Interaction SDK cursor visuals, found {cursorVisuals.Length}.");
            RayInteractorRayVisual[] controllerRayVisuals = rayVisuals
                .Where(visual => IsControllerRayVisual(visual.transform))
                .ToArray();
            RayInteractorCursorVisual[] controllerCursorVisuals = cursorVisuals
                .Where(visual => IsControllerRayVisual(visual.transform))
                .ToArray();
            Require(controllerRayVisuals.Length == 2,
                $"Expected two accepted Touch-controller ray visuals, found {controllerRayVisuals.Length}.");
            Require(controllerCursorVisuals.Length == 2,
                $"Expected two accepted Touch-controller cursor visuals, found {controllerCursorVisuals.Length}.");
            Renderer[] interactionPresentationRenderers = controllerRayVisuals
                .SelectMany(visual => visual.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer != null)
                .Distinct()
                .ToArray();
            Require(interactionPresentationRenderers.Length == 2,
                $"Expected two accepted Touch beam renderers, found {interactionPresentationRenderers.Length}.");
            Require(interactionPresentationRenderers.All(renderer =>
                    renderer.name == "Cube" &&
                    renderer.GetComponentInParent<RayInteractorRayVisual>() != null),
                "Only Touch beam renderers may bypass startup presentation suppression.");
            var interactionRendererSet =
                new System.Collections.Generic.HashSet<Renderer>(
                    interactionPresentationRenderers);
            Renderer[] aircraftPresentationRenderers = aircraftRoot
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null &&
                    !renderer.transform.IsChildOf(presentation.transform) &&
                    !interactionRendererSet.Contains(renderer))
                .ToArray();
            Require(aircraftPresentationRenderers.Length > 0,
                "The accepted aircraft presentation contains no serialized renderers.");

            view.ConfigureAuthoredReferences(
                showroomContent,
                selection,
                transition,
                status,
                transitionText,
                fadeRenderer,
                systems,
                new[]
                {
                    demoButton,
                    freeFlightButton,
                    lessonsButton,
                    calibrationButton
                },
                aircraftPresentationRenderers,
                interactionPresentationRenderers);
            EditorUtility.SetDirty(view);
            presentation.SetActive(false);
            ValidatePresentation(presentation);
            return view;
        }

        private static bool IsControllerRayVisual(Transform visual)
        {
            for (Transform current = visual; current != null; current = current.parent)
            {
                if (current.name != "ControllerRayInteractor") continue;
                return current.parent != null &&
                       current.parent.name == "Controller" &&
                       current.parent.parent != null &&
                       current.parent.parent.name == "Interactors";
            }
            return false;
        }

        public static void BindWorldAuthority(
            ProductFlightModeCoordinator coordinator,
            ProductFlightModeMenuView view)
        {
            ProductFlightModeMenuButton[] buttons = view
                .GetComponentsInChildren<ProductFlightModeMenuButton>(true);
            Require(buttons.Length == 4,
                $"Expected four startup showroom buttons, found {buttons.Length}.");
            foreach (ProductFlightModeMenuButton button in buttons)
            {
                button.BindCoordinator(coordinator);
                EditorUtility.SetDirty(button);
            }
        }

        public static void ValidatePresentation(GameObject presentation)
        {
            ProductFlightModeMenuView[] views = presentation
                .GetComponentsInChildren<ProductFlightModeMenuView>(true);
            Require(views.Length == 1,
                $"Expected one startup showroom view, found {views.Length}.");
            Require(views[0].ValidateAuthoredReferences(out string viewError),
                viewError);
            ProductFlightModeMenuButton[] buttons = presentation
                .GetComponentsInChildren<ProductFlightModeMenuButton>(true);
            Require(buttons.Length == 4,
                $"Expected four startup showroom buttons, found {buttons.Length}.");
            Require(buttons.Select(button => button.Action).Distinct().Count() == 4,
                "Each startup showroom action must be represented exactly once.");
            Require(presentation.GetComponentsInChildren<Camera>(true).Length == 0,
                "The startup showroom must not contain a camera.");
            Require(presentation.GetComponentsInChildren<Rigidbody>(true).Length == 0,
                "The startup showroom must not contain a Rigidbody.");
            Require(presentation.GetComponentsInChildren<Collider>(true).Length == 4,
                "Only the four menu cards may own showroom colliders.");
            Transform backdrop = presentation.transform
                .GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(value => value.name == BackdropName);
            Require(backdrop != null && backdrop.GetComponent<Renderer>() != null,
                "The serialized showroom backdrop is missing.");
            Require(backdrop.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
                "The showroom backdrop must be renderer-only.");
            Transform shell = presentation.transform
                .GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(value => value.name == ShellName);
            Require(shell != null && shell.GetComponent<Renderer>() != null,
                "The serialized inward-facing showroom shell is missing.");
            Require(shell.GetComponentsInChildren<Collider>(true).Length == 0,
                "The showroom shell must not contain a collider.");
        }

        private static ProductFlightModeMenuButton CreateMenuButton(
            Transform parent,
            TextMesh template,
            ProductFlightModeMenuView view,
            string name,
            string title,
            string subtitle,
            Vector3 position,
            ProductFlightModeMenuButton.MenuAction action,
            Material material,
            ProductionSeatCalibrationController seat,
            OvrPointerSourceRegistry registry,
            AudioSource audio,
            Color normal,
            Color hover,
            Color pressed,
            Vector3? sizeOverride = null)
        {
            Vector3 size = sizeOverride ?? new Vector3(1.26f, 0.19f, 0.035f);
            GameObject card = CreateBox(
                name,
                parent,
                position,
                size,
                material,
                removeCollider: false);
            BoxCollider collider = card.GetComponent<BoxCollider>();

            var pokeObject = new GameObject("PokeInteractable");
            pokeObject.transform.SetParent(card.transform, false);
            PlaneSurface plane = pokeObject.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, true);
            BoundsClipper clipper = pokeObject.AddComponent<BoundsClipper>();
            clipper.Size = collider.size;
            ClippedPlaneSurface clipped =
                pokeObject.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(
                plane,
                new IBoundsClipper[] { clipper });
            PokeInteractable poke = pokeObject.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);

            var rayObject = new GameObject("RayInteractable");
            rayObject.transform.SetParent(card.transform, false);
            ColliderSurface surface = rayObject.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(collider);
            RayInteractable ray = rayObject.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(surface);

            ProductFlightModeMenuButton button =
                card.AddComponent<ProductFlightModeMenuButton>();
            button.ConfigureLocalReferences(
                action,
                view,
                seat,
                registry,
                card.GetComponent<Renderer>(),
                audio,
                normal,
                Color.Lerp(normal, hover, 0.58f),
                hover,
                pressed,
                new MonoBehaviour[] { poke, ray });

            float frontZ = position.z - size.z * 0.5f - 0.004f;
            float leftX = position.x - size.x * 0.5f + 0.07f;
            float titleY = string.IsNullOrEmpty(subtitle)
                ? position.y
                : position.y + 0.035f;
            CreateText(
                name + "Title",
                parent,
                template,
                title,
                new Vector3(leftX, titleY, frontZ),
                0.0068f,
                56,
                White,
                TextAnchor.MiddleLeft,
                TextAlignment.Left);
            if (!string.IsNullOrEmpty(subtitle))
            {
                CreateText(
                    name + "Subtitle",
                    parent,
                    template,
                    subtitle,
                    new Vector3(leftX, position.y - 0.052f, frontZ),
                    0.0036f,
                    44,
                    action == ProductFlightModeMenuButton.MenuAction.Lessons
                        ? new Color(1f, 0.62f, 0.20f, 1f)
                        : Muted,
                    TextAnchor.MiddleLeft,
                    TextAlignment.Left);
            }

            return button;
        }

        private static TextMesh CreateText(
            string name,
            Transform parent,
            TextMesh template,
            string value,
            Vector3 position,
            float characterSize,
            int fontSize,
            Color color,
            TextAnchor anchor = TextAnchor.UpperLeft,
            TextAlignment alignment = TextAlignment.Left)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = position;
            TextMesh text = textObject.AddComponent<TextMesh>();
            CopyTextStyle(template, text);
            text.text = value;
            text.characterSize = characterSize;
            text.fontSize = fontSize;
            text.anchor = anchor;
            text.alignment = alignment;
            text.color = color;
            text.richText = false;
            text.tabSize = 4f;
            return text;
        }

        private static void CopyTextStyle(TextMesh source, TextMesh target)
        {
            target.font = source.font;
            target.fontStyle = source.fontStyle;
            target.lineSpacing = source.lineSpacing;
            target.offsetZ = source.offsetZ;
            target.GetComponent<MeshRenderer>().sharedMaterial =
                RequireAsset<Material>(TextMaterialPath);
            target.GetComponent<MeshRenderer>().shadowCastingMode =
                ShadowCastingMode.Off;
            target.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        private static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material,
            bool removeCollider)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = scale;
            MeshRenderer renderer = box.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (removeCollider)
                Object.DestroyImmediate(box.GetComponent<BoxCollider>());
            return box;
        }

        private static GameObject CreateQuad(
            string name,
            Transform parent,
            Vector3 position,
            Vector2 size,
            Material material,
            bool removeCollider)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (removeCollider)
                Object.DestroyImmediate(quad.GetComponent<Collider>());
            return quad;
        }

        private static GameObject CreateSphere(
            string name,
            Transform parent,
            Vector3 position,
            float diameter,
            Material material)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(parent, false);
            sphere.transform.localPosition = position;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = Vector3.one * diameter;
            MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Object.DestroyImmediate(sphere.GetComponent<SphereCollider>());
            return sphere;
        }

        private static void EnsureMaterial(
            string path,
            Shader shader,
            Color color,
            Color topColor,
            float gradient,
            CullMode cull,
            CompareFunction zTest,
            bool zWrite,
            float opacity,
            int renderQueue,
            Texture mainTexture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            SetColorIfPresent(material, "_Color", color);
            SetColorIfPresent(material, "_TopColor", topColor);
            SetFloatIfPresent(material, "_GradientStrength", gradient);
            SetFloatIfPresent(material, "_Cull", (float)cull);
            SetFloatIfPresent(material, "_ZTest", (float)zTest);
            SetFloatIfPresent(material, "_ZWrite", zWrite ? 1f : 0f);
            SetFloatIfPresent(material, "_Opacity", opacity);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", mainTexture);
            SetFloatIfPresent(
                material,
                "_UseMainTex",
                mainTexture != null ? 1f : 0f);
            material.renderQueue = renderQueue;
            EditorUtility.SetDirty(material);
        }

        private static void SetColorIfPresent(
            Material material,
            string property,
            Color value)
        {
            if (material.HasProperty(property))
                material.SetColor(property, value);
        }

        private static void SetFloatIfPresent(
            Material material,
            string property,
            float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static void ConfigureBackdropTexture()
        {
            AssetDatabase.ImportAsset(
                BackdropTexturePath,
                ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(BackdropTexturePath)
                as TextureImporter;
            Require(importer != null,
                "The startup showroom backdrop texture importer is unavailable.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void EnsureTextMaterial(TextMesh template)
        {
            Material source = template.GetComponent<MeshRenderer>().sharedMaterial;
            Require(source != null, "The seat-calibration font material is missing.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TextMaterialPath);
            if (material == null)
            {
                material = new Material(source) { name = "StartupShowroomText" };
                AssetDatabase.CreateAsset(material, TextMaterialPath);
            }
            material.shader = source.shader;
            material.mainTexture = source.mainTexture;
            material.color = Color.white;
            material.renderQueue = 3100;
            EditorUtility.SetDirty(material);
        }

        private static T RequireAsset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Require(asset != null, "Required startup showroom asset is missing: " + path);
            return asset;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
