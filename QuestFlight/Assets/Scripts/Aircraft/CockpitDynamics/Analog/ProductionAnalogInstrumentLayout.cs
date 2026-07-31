using System;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.Analog
{
    /// <summary>
    /// Only the positively identified, currently authoritative analog channels.
    /// An enum member is not added until both source geometry and state authority
    /// have been proven.
    /// </summary>
    public enum AnalogInstrumentFunction
    {
        IndicatedAirspeedKnots = 0,
        IndicatedVerticalSpeedFeetPerMinute = 1
    }

    [Serializable]
    public struct AnalogInstrumentMeshBinding
    {
        public string label;
        public AnalogInstrumentFunction function;
        public int firstVertex;
        public int vertexCount;
        public Vector3 pivotLocal;
        public Vector3 rotationAxisLocal;
        public Vector3 dialRightLocal;
        public Vector3 dialUpLocal;
        public Vector3 authoredIndicatorDirectionLocal;
        public int sourceFaceTriangleSeed;
        public int sourceTipTriangleSeed;
        public int sourceTriangleCount;
    }

    /// <summary>
    /// Generated, serialized contract between the frozen source meter topology
    /// and the one-draw dynamic instrument renderer. It does not create scene
    /// hierarchy or discover runtime objects.
    /// </summary>
    public sealed class ProductionAnalogInstrumentLayout : ScriptableObject
    {
        [SerializeField] private Mesh neutralizedMeterMesh;
        [SerializeField] private Mesh dynamicInstrumentMesh;
        [SerializeField] private Material sharedSourceMeterMaterial;
        [SerializeField] private AnalogInstrumentMeshBinding[] bindings =
            Array.Empty<AnalogInstrumentMeshBinding>();
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private int neutralizedTriangleCount;
        [SerializeField] private int dynamicTriangleCount;
        [SerializeField] private string sourceMeshName = string.Empty;
        [SerializeField] private string sourceAssetGuid = string.Empty;

        public Mesh NeutralizedMeterMesh => neutralizedMeterMesh;
        public Mesh DynamicInstrumentMesh => dynamicInstrumentMesh;
        public Material SharedSourceMeterMaterial => sharedSourceMeterMaterial;
        public AnalogInstrumentMeshBinding[] Bindings => bindings;
        public int SourceTriangleCount => sourceTriangleCount;
        public int NeutralizedTriangleCount => neutralizedTriangleCount;
        public int DynamicTriangleCount => dynamicTriangleCount;
        public string SourceMeshName => sourceMeshName;
        public string SourceAssetGuid => sourceAssetGuid;

        public void ConfigureGeneratedAssets(
            Mesh authoredNeutralizedMeterMesh,
            Mesh authoredDynamicInstrumentMesh,
            Material authoredSharedSourceMeterMaterial,
            AnalogInstrumentMeshBinding[] authoredBindings,
            int authoredSourceTriangleCount,
            int authoredNeutralizedTriangleCount,
            int authoredDynamicTriangleCount,
            string authoredSourceMeshName,
            string authoredSourceAssetGuid)
        {
            neutralizedMeterMesh = authoredNeutralizedMeterMesh;
            dynamicInstrumentMesh = authoredDynamicInstrumentMesh;
            sharedSourceMeterMaterial = authoredSharedSourceMeterMaterial;
            bindings = authoredBindings ?? Array.Empty<AnalogInstrumentMeshBinding>();
            sourceTriangleCount = authoredSourceTriangleCount;
            neutralizedTriangleCount = authoredNeutralizedTriangleCount;
            dynamicTriangleCount = authoredDynamicTriangleCount;
            sourceMeshName = authoredSourceMeshName ?? string.Empty;
            sourceAssetGuid = authoredSourceAssetGuid ?? string.Empty;
        }

        public bool Validate(out string error)
        {
            if (neutralizedMeterMesh == null)
            {
                error = "The neutralized source meter mesh is missing.";
                return false;
            }
            if (dynamicInstrumentMesh == null)
            {
                error = "The combined dynamic instrument mesh is missing.";
                return false;
            }
            if (sharedSourceMeterMaterial == null)
            {
                error = "The existing source meter material is missing.";
                return false;
            }
            if (bindings == null || bindings.Length != 2)
            {
                error =
                    $"Exactly two identity-and-calibration-audited analog bindings are " +
                    $"required; found " +
                    $"{(bindings == null ? 0 : bindings.Length)}.";
                return false;
            }
            if (neutralizedMeterMesh.subMeshCount != 1 ||
                dynamicInstrumentMesh.subMeshCount != 1)
            {
                error = "Neutralized and dynamic meter meshes must each use one submesh.";
                return false;
            }
            if (sourceTriangleCount <= 0 ||
                neutralizedTriangleCount + dynamicTriangleCount != sourceTriangleCount)
            {
                error =
                    "Neutralized and dynamic triangle partitions do not reconstruct the " +
                    "source meter topology.";
                return false;
            }
            if ((int)(neutralizedMeterMesh.GetIndexCount(0) / 3u) !=
                    neutralizedTriangleCount ||
                (int)(dynamicInstrumentMesh.GetIndexCount(0) / 3u) !=
                    dynamicTriangleCount)
            {
                error = "Serialized analog triangle counts do not match the mesh assets.";
                return false;
            }
            if (dynamicInstrumentMesh.vertexCount != dynamicTriangleCount * 3)
            {
                error =
                    "The combined analog mesh must contain one unique vertex per " +
                    "source-triangle corner.";
                return false;
            }

            bool[] functions = new bool[2];
            int expectedFirstVertex = 0;
            int triangleTotal = 0;
            for (int index = 0; index < bindings.Length; index++)
            {
                AnalogInstrumentMeshBinding binding = bindings[index];
                int functionIndex = (int)binding.function;
                if (functionIndex < 0 || functionIndex >= functions.Length ||
                    functions[functionIndex])
                {
                    error =
                        $"Analog binding {index} has a duplicate or unsupported function.";
                    return false;
                }
                functions[functionIndex] = true;
                if (binding.firstVertex != expectedFirstVertex ||
                    binding.vertexCount <= 0 ||
                    binding.vertexCount % 3 != 0)
                {
                    error =
                        $"Analog binding '{binding.label}' has a non-contiguous vertex range.";
                    return false;
                }
                if (binding.sourceTriangleCount * 3 != binding.vertexCount)
                {
                    error =
                        $"Analog binding '{binding.label}' does not preserve its source triangles.";
                    return false;
                }
                if (binding.rotationAxisLocal.sqrMagnitude < 0.99f ||
                    binding.dialRightLocal.sqrMagnitude < 0.99f ||
                    binding.dialUpLocal.sqrMagnitude < 0.99f)
                {
                    error =
                        $"Analog binding '{binding.label}' has an invalid dial basis.";
                    return false;
                }
                expectedFirstVertex += binding.vertexCount;
                triangleTotal += binding.sourceTriangleCount;
            }
            if (expectedFirstVertex != dynamicInstrumentMesh.vertexCount ||
                triangleTotal != dynamicTriangleCount)
            {
                error = "Analog binding ranges do not cover the combined dynamic mesh once.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
