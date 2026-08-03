using System;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.V2
{
    [Flags]
    public enum CockpitInstrumentV2Availability : ushort
    {
        None = 0,
        AirData = 1 << 0,
        BarometricSetting = 1 << 1,
        Attitude = 1 << 2,
        TrueHeading = 1 << 3,
        MagneticVariation = 1 << 4,
        ActualFlapPosition = 1 << 5,
        BrakeHydraulics = 1 << 6,
        ClockUtc = 1 << 7,
        EngineRpm = 1 << 8,
        FuelSystem = 1 << 9,
        OilSystem = 1 << 10,
        ElectricalIndications = 1 << 11,
        VacuumSuction = 1 << 12,
        TurnAndSlip = 1 << 13
    }

    /// <summary>
    /// Explicit authority boundary for represented V2 instruments. A field is
    /// unusable unless its availability bit is present. This prevents a control
    /// command from being relabeled as measured pressure or surface position.
    /// </summary>
    [Serializable]
    public struct CockpitInstrumentV2Snapshot
    {
        public bool isValid;
        public CockpitInstrumentV2Availability availability;
        // Standard-datum pressure altitude. The barometric setting is applied
        // here; geometric/MSL altitude must not be substituted.
        public float pressureAltitudeFeet;
        public float indicatedAirspeedKnots;
        public float indicatedVerticalSpeedFeetPerMinute;
        public float barometricSettingInHg;
        public float pitchDegrees;
        public float bankDegrees;
        public float trueHeadingDegrees;
        public float magneticVariationEastDegrees;
        public float actualFlapDegrees;
        public float leftBrakePressurePsi;
        public float rightBrakePressurePsi;
        public double clockUtcSeconds;
        public float engineRpm;
        public float leftFuelGallons;
        public float rightFuelGallons;
        public float fuelFlowGallonsPerHour;
        public float oilPressurePsi;
        public float oilTemperatureFahrenheit;
        public float electricalBusVolts;
        public float alternatorAmps;
        public float vacuumInHg;
        public float turnRateDegreesPerSecond;
        public float slipSkidNormalized;

        public bool Has(CockpitInstrumentV2Availability required)
        {
            return isValid && (availability & required) == required;
        }
    }

    public interface ICockpitInstrumentV2AuthorityProvider
    {
        bool TryGetInstrumentV2Snapshot(out CockpitInstrumentV2Snapshot snapshot);
    }

    public enum CockpitInstrumentV2Function
    {
        None = 0,
        IndicatedAirspeedNeedleDegrees = 1,
        IndicatedVerticalSpeedNeedleDegrees = 2,
        AltimeterHundredsNeedleDegrees = 3,
        AltimeterThousandsNeedleDegrees = 4,
        AltimeterTenThousandsNeedleDegrees = 5,
        AltimeterBarometricDrumInHg = 6,
        AttitudeBankDegrees = 10,
        AttitudePitchDegrees = 11,
        DirectionalGyroHeadingDegrees = 12,
        MagneticCompassHeadingDegrees = 13,
        ActualFlapDegrees = 20,
        AverageBrakePressurePsi = 21,
        EngineRpmNeedleDegrees = 22,
        TurnCoordinatorBankDegrees = 23,
        SlipSkidNormalized = 24,
        ClockHourHandDegrees = 30,
        ClockMinuteHandDegrees = 31,
        ClockSecondHandDegrees = 32
    }

    /// <summary>
    /// A contiguous vertex range within the single combined dynamic instrument
    /// mesh. The range can receive one rotation and one translation, allowing an
    /// attitude card to combine bank and pitch without another renderer.
    /// </summary>
    [Serializable]
    public struct CockpitInstrumentV2MeshBinding
    {
        public string label;
        public string sourceIdentity;
        public int sourceTriangleSeed;
        public int firstVertex;
        public int vertexCount;
        public Vector3 pivotLocal;
        public Vector3 rotationAxisLocal;
        public CockpitInstrumentV2Function rotationFunction;
        public float authoredRotationValue;
        public float rotationDegreesPerUnit;
        public Vector3 translationAxisLocal;
        public CockpitInstrumentV2Function translationFunction;
        public float authoredTranslationValue;
        public float translationMetersPerUnit;
        public bool absoluteRegistrationProven;
        public bool magneticVariationContractProven;

        public bool HasRotation =>
            rotationFunction != CockpitInstrumentV2Function.None &&
            rotationAxisLocal.sqrMagnitude > 0.999f &&
            IsFiniteNonZero(rotationDegreesPerUnit);

        public bool HasTranslation =>
            translationFunction != CockpitInstrumentV2Function.None &&
            translationAxisLocal.sqrMagnitude > 0.999f &&
            IsFiniteNonZero(translationMetersPerUnit);

        private static bool IsFiniteNonZero(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   Mathf.Abs(value) > 0.000001f;
        }
    }

    /// <summary>
    /// One decimal position in the principal altimeter's frozen odometer
    /// aperture. The six de-indexed quad vertices stay in the existing single
    /// instrument batch; only their UVs change at runtime.
    /// </summary>
    [Serializable]
    public struct CockpitInstrumentV2AltitudeDigitSlot
    {
        public string label;
        public int firstVertex;
        public int vertexCount;
        public int placeValue;
    }

    /// <summary>
    /// Frozen topology contract for the existing attitude-indicator gyro ball.
    /// The ball remains in the neutralized source mesh and is selected in the
    /// vertex shader, so pitch and bank add no renderer, material, or mesh
    /// upload. The miniature-aircraft reference geometry is deliberately not
    /// part of this contract and therefore remains fixed to the case.
    /// </summary>
    [Serializable]
    public struct CockpitInstrumentV2AttitudeContract
    {
        public bool enabled;
        public int[] sourceComponentSeeds;
        public int sourceVertexCount;
        public int sourceTriangleCount;
        public Vector3 pivotLocal;
        public Vector3 pitchAxisLocal;
        public Vector3 bankAxisLocal;
        public float sphereRadiusLocal;
        public float sphereFitRmsLocal;

        public bool Validate(out string error)
        {
            if (!enabled)
            {
                error = "The represented attitude-indicator gyro ball is disabled.";
                return false;
            }
            if (sourceComponentSeeds == null ||
                sourceComponentSeeds.Length != 5 ||
                sourceVertexCount != 998 ||
                sourceTriangleCount != 1740)
            {
                error = "The frozen attitude-indicator topology contract changed.";
                return false;
            }
            if (pitchAxisLocal.sqrMagnitude < 0.999f ||
                bankAxisLocal.sqrMagnitude < 0.999f ||
                Mathf.Abs(Vector3.Dot(
                    pitchAxisLocal.normalized,
                    bankAxisLocal.normalized)) > 0.001f)
            {
                error = "The attitude-indicator axes are not an orthonormal frame.";
                return false;
            }
            if (sphereRadiusLocal <= 0f ||
                float.IsNaN(sphereRadiusLocal) ||
                float.IsInfinity(sphereRadiusLocal) ||
                sphereFitRmsLocal < 0f ||
                sphereFitRmsLocal > 0.00006f)
            {
                error = "The attitude-indicator sphere fit is outside tolerance.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Generated Editor-time partition of represented source geometry. The V2
    /// controller consumes exactly one neutralized static renderer and one
    /// combined dynamic renderer using the same existing source material.
    /// </summary>
    public sealed class CockpitInstrumentV2Layout : ScriptableObject
    {
        [SerializeField] private Mesh neutralizedSourceMesh;
        [SerializeField] private Mesh combinedDynamicMesh;
        [SerializeField] private Material existingSourceMaterial;
        [SerializeField] private CockpitInstrumentV2MeshBinding[] bindings =
            Array.Empty<CockpitInstrumentV2MeshBinding>();
        [SerializeField]
        private CockpitInstrumentV2AltitudeDigitSlot[] altitudeDigitSlots =
            Array.Empty<CockpitInstrumentV2AltitudeDigitSlot>();
        [SerializeField] private Rect[] altitudeDigitUvRects =
            Array.Empty<Rect>();
        [SerializeField]
        private CockpitInstrumentV2AttitudeContract attitudeContract;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private int neutralizedTriangleCount;
        [SerializeField] private int dynamicTriangleCount;
        [SerializeField] private int staticOverlayVertexCount;
        [SerializeField] private bool opaqueOverlayCoversSourceApertures;
        [SerializeField] private string sourceAssetGuid = string.Empty;
        [SerializeField] private string sourceSha256 = string.Empty;

        public Mesh NeutralizedSourceMesh => neutralizedSourceMesh;
        public Mesh CombinedDynamicMesh => combinedDynamicMesh;
        public Material ExistingSourceMaterial => existingSourceMaterial;
        public CockpitInstrumentV2MeshBinding[] Bindings => bindings;
        public CockpitInstrumentV2AltitudeDigitSlot[] AltitudeDigitSlots =>
            altitudeDigitSlots;
        public Rect[] AltitudeDigitUvRects => altitudeDigitUvRects;
        public CockpitInstrumentV2AttitudeContract AttitudeContract =>
            attitudeContract;
        public int SourceTriangleCount => sourceTriangleCount;
        public int NeutralizedTriangleCount => neutralizedTriangleCount;
        public int DynamicTriangleCount => dynamicTriangleCount;
        public int StaticOverlayVertexCount => staticOverlayVertexCount;
        public bool OpaqueOverlayCoversSourceApertures =>
            opaqueOverlayCoversSourceApertures;
        public string SourceAssetGuid => sourceAssetGuid;
        public string SourceSha256 => sourceSha256;

        public void ConfigureGenerated(
            Mesh authoredNeutralizedSourceMesh,
            Mesh authoredCombinedDynamicMesh,
            Material authoredExistingSourceMaterial,
            CockpitInstrumentV2MeshBinding[] authoredBindings,
            int authoredSourceTriangleCount,
            int authoredNeutralizedTriangleCount,
            int authoredDynamicTriangleCount,
            string authoredSourceAssetGuid,
            string authoredSourceSha256)
        {
            ConfigureGenerated(
                authoredNeutralizedSourceMesh,
                authoredCombinedDynamicMesh,
                authoredExistingSourceMaterial,
                authoredBindings,
                authoredSourceTriangleCount,
                authoredNeutralizedTriangleCount,
                authoredDynamicTriangleCount,
                authoredSourceAssetGuid,
                authoredSourceSha256,
                0,
                false,
                Array.Empty<CockpitInstrumentV2AltitudeDigitSlot>(),
                Array.Empty<Rect>());
        }

        public void ConfigureGenerated(
            Mesh authoredNeutralizedSourceMesh,
            Mesh authoredCombinedDynamicMesh,
            Material authoredExistingSourceMaterial,
            CockpitInstrumentV2MeshBinding[] authoredBindings,
            int authoredSourceTriangleCount,
            int authoredNeutralizedTriangleCount,
            int authoredDynamicTriangleCount,
            string authoredSourceAssetGuid,
            string authoredSourceSha256,
            int authoredStaticOverlayVertexCount,
            bool authoredOpaqueOverlayCoversSourceApertures,
            CockpitInstrumentV2AltitudeDigitSlot[] authoredAltitudeDigitSlots,
            Rect[] authoredAltitudeDigitUvRects)
        {
            neutralizedSourceMesh = authoredNeutralizedSourceMesh;
            combinedDynamicMesh = authoredCombinedDynamicMesh;
            existingSourceMaterial = authoredExistingSourceMaterial;
            bindings = authoredBindings ?? Array.Empty<CockpitInstrumentV2MeshBinding>();
            sourceTriangleCount = authoredSourceTriangleCount;
            neutralizedTriangleCount = authoredNeutralizedTriangleCount;
            dynamicTriangleCount = authoredDynamicTriangleCount;
            sourceAssetGuid = authoredSourceAssetGuid ?? string.Empty;
            sourceSha256 = authoredSourceSha256 ?? string.Empty;
            staticOverlayVertexCount = authoredStaticOverlayVertexCount;
            opaqueOverlayCoversSourceApertures =
                authoredOpaqueOverlayCoversSourceApertures;
            altitudeDigitSlots = authoredAltitudeDigitSlots ??
                Array.Empty<CockpitInstrumentV2AltitudeDigitSlot>();
            altitudeDigitUvRects = authoredAltitudeDigitUvRects ??
                Array.Empty<Rect>();
        }

        public void ConfigureAttitudeContract(
            CockpitInstrumentV2AttitudeContract authoredContract)
        {
            attitudeContract = authoredContract;
        }

        public bool Validate(out string error)
        {
            if (neutralizedSourceMesh == null || combinedDynamicMesh == null)
            {
                error = "V2 instrument source partitions are missing.";
                return false;
            }
            if (existingSourceMaterial == null)
            {
                error = "V2 instruments must reuse the existing source material.";
                return false;
            }
            if (neutralizedSourceMesh.subMeshCount != 1 ||
                combinedDynamicMesh.subMeshCount != 1)
            {
                error = "V2 instrument partitions require one submesh each.";
                return false;
            }
            if (sourceTriangleCount <= 0 ||
                neutralizedTriangleCount < 0 ||
                dynamicTriangleCount <= 0)
            {
                error = "V2 instrument triangle accounting is invalid.";
                return false;
            }
            if ((int)(neutralizedSourceMesh.GetIndexCount(0) / 3u) !=
                    neutralizedTriangleCount ||
                (int)(combinedDynamicMesh.GetIndexCount(0) / 3u) !=
                    dynamicTriangleCount)
            {
                error = "V2 serialized triangle counts do not match the meshes.";
                return false;
            }
            if (staticOverlayVertexCount < 0 ||
                staticOverlayVertexCount > combinedDynamicMesh.vertexCount ||
                staticOverlayVertexCount % 3 != 0)
            {
                error = "V2 static overlay vertex prefix is invalid.";
                return false;
            }
            if (opaqueOverlayCoversSourceApertures)
            {
                if (staticOverlayVertexCount == 0)
                {
                    error =
                        "Opaque overlay presentation requires a non-empty static prefix.";
                    return false;
                }
                if (neutralizedTriangleCount > sourceTriangleCount)
                {
                    error =
                        "The neutralized source cannot contain more triangles than its source.";
                    return false;
                }
            }
            else if (staticOverlayVertexCount != 0 ||
                     neutralizedTriangleCount + dynamicTriangleCount !=
                         sourceTriangleCount)
            {
                error =
                    "A source partition without an overlay must reconstruct the source.";
                return false;
            }
            bool hasAltitudeDigitContract =
                (altitudeDigitUvRects != null &&
                 altitudeDigitUvRects.Length > 0) ||
                (altitudeDigitSlots != null && altitudeDigitSlots.Length > 0);
            if (hasAltitudeDigitContract &&
                (altitudeDigitUvRects == null ||
                 altitudeDigitUvRects.Length != 10))
            {
                error = "The altimeter digit atlas must provide 0-9 UV cells.";
                return false;
            }
            if (hasAltitudeDigitContract &&
                (altitudeDigitSlots == null ||
                 altitudeDigitSlots.Length != 4))
            {
                error = "The principal altimeter requires four odometer digits.";
                return false;
            }
            int expectedPlace = 1000;
            for (int index = 0;
                 hasAltitudeDigitContract && index < altitudeDigitSlots.Length;
                 index++)
            {
                CockpitInstrumentV2AltitudeDigitSlot slot =
                    altitudeDigitSlots[index];
                if (string.IsNullOrWhiteSpace(slot.label) ||
                    slot.vertexCount != 6 ||
                    slot.firstVertex < 0 ||
                    slot.firstVertex + slot.vertexCount >
                        staticOverlayVertexCount ||
                    slot.placeValue != expectedPlace)
                {
                    error = $"Altimeter digit slot {index} is malformed.";
                    return false;
                }
                expectedPlace /= 10;
            }
            if (string.IsNullOrWhiteSpace(sourceAssetGuid) ||
                sourceSha256 == null || sourceSha256.Length != 64)
            {
                error = "V2 instrument source identity is incomplete.";
                return false;
            }
            if (bindings == null || bindings.Length == 0)
            {
                error = "V2 instrument bindings are missing.";
                return false;
            }
            if (!attitudeContract.Validate(out error))
                return false;

            var sourceSelector = new System.Collections.Generic.List<Vector4>();
            neutralizedSourceMesh.GetUVs(3, sourceSelector);
            if (sourceSelector.Count != neutralizedSourceMesh.vertexCount)
            {
                error = "The attitude-indicator source selector stream is incomplete.";
                return false;
            }
            int attitudeVertexCount = 0;
            for (int index = 0; index < sourceSelector.Count; index++)
            {
                if (sourceSelector[index].z > 0.5f)
                    attitudeVertexCount++;
            }
            if (attitudeVertexCount != attitudeContract.sourceVertexCount)
            {
                error = "The attitude-indicator shader mask does not match its frozen topology.";
                return false;
            }

            int expectedFirstVertex = staticOverlayVertexCount;
            bool[] functions = new bool[
                (int)CockpitInstrumentV2Function.ClockSecondHandDegrees + 1];
            for (int index = 0; index < bindings.Length; index++)
            {
                CockpitInstrumentV2MeshBinding binding = bindings[index];
                if (string.IsNullOrWhiteSpace(binding.label) ||
                    string.IsNullOrWhiteSpace(binding.sourceIdentity) ||
                    binding.sourceTriangleSeed < 0)
                {
                    error = $"V2 binding {index} lacks a frozen source identity.";
                    return false;
                }
                if (binding.firstVertex != expectedFirstVertex ||
                    binding.vertexCount <= 0 ||
                    binding.vertexCount % 3 != 0)
                {
                    error = $"V2 binding '{binding.label}' has a bad vertex range.";
                    return false;
                }
                if (!binding.HasRotation && !binding.HasTranslation)
                {
                    error = $"V2 binding '{binding.label}' has no valid motion.";
                    return false;
                }
                if (!RegisterFunction(
                        binding.rotationFunction, functions, out error) ||
                    !RegisterFunction(
                        binding.translationFunction, functions, out error))
                {
                    error = $"V2 binding '{binding.label}': {error}";
                    return false;
                }
                if (Uses(
                        binding,
                        CockpitInstrumentV2Function.DirectionalGyroHeadingDegrees) &&
                    !binding.absoluteRegistrationProven)
                {
                    error =
                        "Directional gyro motion requires proven absolute lubber registration.";
                    return false;
                }
                if (Uses(
                        binding,
                        CockpitInstrumentV2Function.MagneticCompassHeadingDegrees) &&
                    !binding.magneticVariationContractProven)
                {
                    error =
                        "Magnetic compass motion requires an explicit variation contract.";
                    return false;
                }
                expectedFirstVertex += binding.vertexCount;
            }
            if (expectedFirstVertex != combinedDynamicMesh.vertexCount)
            {
                error = "V2 bindings do not cover the dynamic mesh exactly once.";
                return false;
            }
            if (!ValidateCompleteInstrumentGroups(functions, out error))
                return false;

            error = string.Empty;
            return true;
        }

        private static bool RegisterFunction(
            CockpitInstrumentV2Function function,
            bool[] functions,
            out string error)
        {
            if (function == CockpitInstrumentV2Function.None)
            {
                error = string.Empty;
                return true;
            }
            int index = (int)function;
            if (index <= 0 || index >= functions.Length || functions[index])
            {
                error = $"duplicate or unsupported function {function}.";
                return false;
            }
            functions[index] = true;
            error = string.Empty;
            return true;
        }

        private static bool Uses(
            CockpitInstrumentV2MeshBinding binding,
            CockpitInstrumentV2Function function)
        {
            return binding.rotationFunction == function ||
                   binding.translationFunction == function;
        }

        private static bool ValidateCompleteInstrumentGroups(
            bool[] functions,
            out string error)
        {
            bool anyAltimeter =
                functions[(int)CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees] ||
                functions[(int)CockpitInstrumentV2Function.AltimeterThousandsNeedleDegrees] ||
                functions[(int)CockpitInstrumentV2Function.AltimeterTenThousandsNeedleDegrees] ||
                functions[(int)CockpitInstrumentV2Function.AltimeterBarometricDrumInHg];
            bool completeAltimeter =
                functions[(int)CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees] &&
                functions[(int)CockpitInstrumentV2Function.AltimeterThousandsNeedleDegrees] &&
                functions[(int)CockpitInstrumentV2Function.AltimeterTenThousandsNeedleDegrees] &&
                functions[(int)CockpitInstrumentV2Function.AltimeterBarometricDrumInHg];
            if (anyAltimeter && !completeAltimeter)
            {
                error =
                    "A partially animated altimeter is prohibited; needles and baro drum are one contract.";
                return false;
            }

            bool pitch =
                functions[(int)CockpitInstrumentV2Function.AttitudePitchDegrees];
            bool bank =
                functions[(int)CockpitInstrumentV2Function.AttitudeBankDegrees];
            if (pitch != bank)
            {
                error =
                    "Attitude presentation requires both pitch and bank on the same represented card.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
