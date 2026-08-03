using System;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Review disposition for mechanical geometry in the frozen C172 source.
    /// A represented object is not necessarily safe to animate: both its
    /// mechanical layout and a stable presentation technique must be proven.
    /// </summary>
    public enum CockpitMechanicalV2Disposition
    {
        ExistingAuthoredMotion = 0,
        RepresentedStatic = 1,
        FailClosedExcluded = 2
    }

    public enum CockpitMechanicalV2Blocker
    {
        None = 0,
        FlapSelectorHingeArcAndDetentsUnproven = 1,
        PedalSetIncompleteAndHingesUnproven = 2,
        PropellerPhaseWouldAliasAtQuestRefresh = 3,
        TireAndFixedWheelPantShareOneMesh = 4
    }

    public enum CockpitMechanicalV2LayoutKind
    {
        ExistingNoseSteeringAssembly = 0,
        SeparateSourcesOnOneProvenShaftAxis = 1,
        SingleRepresentedSourceWithoutProvenMotion = 2,
        MergedSourceWithoutSafeSeparation = 3,
        AuthoredFourPedalRigidBatch = 4
    }

    public enum CockpitMechanicalV2AxisEvidence
    {
        None = 0,
        AircraftVerticalFromExistingNoseGear = 1,
        MeshLocalPositiveZPropellerShaft = 2,
        SourceSymmetryPlaneAndRigidPedalBounds = 3,
        AuthoredRudderAndToeBrakeHingeAxes = 4
    }

    /// <summary>
    /// Immutable source-identity and performance record. Up to three source
    /// nodes are stored inline to avoid mutable arrays and runtime allocations.
    /// Vertex/triangle counts refer to the frozen imported source meshes.
    /// </summary>
    [Serializable]
    public readonly struct CockpitMechanicalV2SourceDescriptor
    {
        public readonly string id;
        public readonly CockpitMechanicalV2Disposition disposition;
        public readonly CockpitMechanicalV2Blocker blocker;
        public readonly CockpitMechanicalV2LayoutKind layout;
        public readonly CockpitMechanicalV2AxisEvidence axisEvidence;
        public readonly string primarySourcePath;
        public readonly int primaryVertexCount;
        public readonly int primaryTriangleCount;
        public readonly string secondarySourcePath;
        public readonly int secondaryVertexCount;
        public readonly int secondaryTriangleCount;
        public readonly string tertiarySourcePath;
        public readonly int tertiaryVertexCount;
        public readonly int tertiaryTriangleCount;
        public readonly int auditedConnectedComponentCount;
        public readonly int auditedRepresentedAssemblyCount;
        public readonly int rendererDelta;
        public readonly int drawDelta;
        public readonly int materialDelta;
        public readonly int textureDelta;

        public bool HasRuntimeMotion =>
            disposition == CockpitMechanicalV2Disposition.ExistingAuthoredMotion;

        public bool PreservesFrozenSources =>
            rendererDelta == 0 &&
            drawDelta == 0 &&
            materialDelta == 0 &&
            textureDelta == 0;

        public CockpitMechanicalV2SourceDescriptor(
            string id,
            CockpitMechanicalV2Disposition disposition,
            CockpitMechanicalV2Blocker blocker,
            CockpitMechanicalV2LayoutKind layout,
            CockpitMechanicalV2AxisEvidence axisEvidence,
            string primarySourcePath,
            int primaryVertexCount,
            int primaryTriangleCount,
            string secondarySourcePath = "",
            int secondaryVertexCount = 0,
            int secondaryTriangleCount = 0,
            string tertiarySourcePath = "",
            int tertiaryVertexCount = 0,
            int tertiaryTriangleCount = 0,
            int auditedConnectedComponentCount = 0,
            int auditedRepresentedAssemblyCount = 0)
        {
            this.id = id;
            this.disposition = disposition;
            this.blocker = blocker;
            this.layout = layout;
            this.axisEvidence = axisEvidence;
            this.primarySourcePath = primarySourcePath;
            this.primaryVertexCount = primaryVertexCount;
            this.primaryTriangleCount = primaryTriangleCount;
            this.secondarySourcePath = secondarySourcePath;
            this.secondaryVertexCount = secondaryVertexCount;
            this.secondaryTriangleCount = secondaryTriangleCount;
            this.tertiarySourcePath = tertiarySourcePath;
            this.tertiaryVertexCount = tertiaryVertexCount;
            this.tertiaryTriangleCount = tertiaryTriangleCount;
            this.auditedConnectedComponentCount = auditedConnectedComponentCount;
            this.auditedRepresentedAssemblyCount = auditedRepresentedAssemblyCount;
            rendererDelta = 0;
            drawDelta = 0;
            materialDelta = 0;
            textureDelta = 0;
        }
    }

    /// <summary>
    /// Frozen, fail-closed mechanical catalog for the accepted source GLB.
    /// It deliberately contains no mutable product state and performs no
    /// hierarchy lookup or construction.
    /// </summary>
    public static class CockpitMechanicalV2Contract
    {
        public const string SourceGlbGuid =
            "d312be0fd0ba928458dd86cbe967956b";
        public const string SourceGlbSha256 =
            "a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872";

        public const string ExteriorRootPath =
            "AircraftVisualRoot/ImportedAircraftExterior/Cessna_Exterior";
        public const string ImportedCockpitPath =
            "AircraftVisualRoot/ImportedCockpitInterior";
        public const string InteriorRootPath =
            ImportedCockpitPath + "/Cessna_Interior";

        public const string NoseSteeringStrutPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.019";
        public const string NoseWheelPantPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.021";
        public const string AuthoredNoseSteeringStrutPath =
            "AircraftVisualRoot/ProductionNoseSteeringPivot/" +
            "Cessna_Exterior_Body_MAT_0.019";
        public const string AuthoredNoseWheelPantPath =
            "AircraftVisualRoot/ProductionNoseSteeringPivot/" +
            "Cessna_Exterior_Body_MAT_0.021";
        public const string FlapSelectorPath =
            ImportedCockpitPath + "/Dynamic_Switch_007";
        public const string PedalMergedSourcePath =
            InteriorRootPath + "/Cessna_Interior_Paddle_MAT_0";
        public const string PropellerPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.001";
        public const string SpinnerPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.024";
        public const string PositiveXMainWheelPantPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.022";
        public const string NegativeXMainWheelPantPath =
            ExteriorRootPath + "/Cessna_Exterior_Body_MAT_0.023";

        private static readonly CockpitMechanicalV2SourceDescriptor[] Records =
        {
            new CockpitMechanicalV2SourceDescriptor(
                "nose_steering_existing",
                CockpitMechanicalV2Disposition.ExistingAuthoredMotion,
                CockpitMechanicalV2Blocker.None,
                CockpitMechanicalV2LayoutKind.ExistingNoseSteeringAssembly,
                CockpitMechanicalV2AxisEvidence.AircraftVerticalFromExistingNoseGear,
                NoseSteeringStrutPath,
                231,
                164,
                NoseWheelPantPath,
                562,
                814),
            new CockpitMechanicalV2SourceDescriptor(
                "wing_flap_selector_static",
                CockpitMechanicalV2Disposition.RepresentedStatic,
                CockpitMechanicalV2Blocker
                    .FlapSelectorHingeArcAndDetentsUnproven,
                CockpitMechanicalV2LayoutKind
                    .SingleRepresentedSourceWithoutProvenMotion,
                CockpitMechanicalV2AxisEvidence.None,
                FlapSelectorPath,
                128,
                166,
                auditedConnectedComponentCount: 5,
                auditedRepresentedAssemblyCount: 1),
            new CockpitMechanicalV2SourceDescriptor(
                "rudder_toe_brake_pedals_completed",
                CockpitMechanicalV2Disposition.ExistingAuthoredMotion,
                CockpitMechanicalV2Blocker.None,
                CockpitMechanicalV2LayoutKind.AuthoredFourPedalRigidBatch,
                CockpitMechanicalV2AxisEvidence
                    .AuthoredRudderAndToeBrakeHingeAxes,
                PedalMergedSourcePath,
                14635,
                12971,
                auditedConnectedComponentCount: 1229,
                auditedRepresentedAssemblyCount: 4),
            new CockpitMechanicalV2SourceDescriptor(
                "fixed_pitch_propeller_and_spinner_static",
                CockpitMechanicalV2Disposition.RepresentedStatic,
                CockpitMechanicalV2Blocker
                    .PropellerPhaseWouldAliasAtQuestRefresh,
                CockpitMechanicalV2LayoutKind
                    .SeparateSourcesOnOneProvenShaftAxis,
                CockpitMechanicalV2AxisEvidence
                    .MeshLocalPositiveZPropellerShaft,
                PropellerPath,
                166,
                212,
                SpinnerPath,
                76,
                120),
            new CockpitMechanicalV2SourceDescriptor(
                "fixed_gear_wheel_roll_excluded",
                CockpitMechanicalV2Disposition.FailClosedExcluded,
                CockpitMechanicalV2Blocker
                    .TireAndFixedWheelPantShareOneMesh,
                CockpitMechanicalV2LayoutKind
                    .MergedSourceWithoutSafeSeparation,
                CockpitMechanicalV2AxisEvidence.None,
                NoseWheelPantPath,
                562,
                814,
                PositiveXMainWheelPantPath,
                578,
                834,
                NegativeXMainWheelPantPath,
                588,
                834)
        };

        public static int RecordCount => Records.Length;

        public static CockpitMechanicalV2SourceDescriptor GetRecord(int index)
        {
            if (index < 0 || index >= Records.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return Records[index];
        }

        public static bool TryGetRecord(
            string id,
            out CockpitMechanicalV2SourceDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(id))
            {
                descriptor = default;
                return false;
            }

            for (int index = 0; index < Records.Length; index++)
            {
                if (!string.Equals(
                        Records[index].id,
                        id,
                        StringComparison.Ordinal))
                    continue;
                descriptor = Records[index];
                return true;
            }

            descriptor = default;
            return false;
        }
    }
}
