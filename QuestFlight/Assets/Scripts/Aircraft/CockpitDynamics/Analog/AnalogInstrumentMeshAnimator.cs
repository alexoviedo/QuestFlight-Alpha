using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Aircraft.CockpitDynamics.Analog
{
    /// <summary>
    /// Allocation-free CPU vertex animator for the single combined analog mesh.
    /// All arrays/lists are allocated once during authored controller startup.
    /// </summary>
    public sealed class AnalogInstrumentMeshAnimator : IDisposable
    {
        private const MeshUpdateFlags UploadFlags =
            MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices |
            MeshUpdateFlags.DontNotifyMeshUsers;

        private readonly AnalogInstrumentMeshBinding[] _bindings;
        private readonly Vector3[] _baseVertices;
        private readonly Vector3[] _baseNormals;
        private readonly Vector4[] _baseTangents;
        private readonly List<Vector3> _workingVertices;
        private readonly List<Vector3> _workingNormals;
        private readonly List<Vector4> _workingTangents;
        private QuestFlight.Rendering.ProductDeformedMeshHistory motionHistory;
        public void Dispose() => motionHistory?.Dispose();
        private readonly float[] _lastDialAngles = new float[2];

        public AnalogInstrumentMeshAnimator(
            Mesh authoredDynamicMesh,
            AnalogInstrumentMeshBinding[] authoredBindings)
        {
            if (authoredDynamicMesh == null)
                throw new ArgumentNullException(nameof(authoredDynamicMesh));
            if (authoredBindings == null || authoredBindings.Length == 0)
                throw new ArgumentException(
                    "Authored analog bindings are required.", nameof(authoredBindings));

            _bindings = authoredBindings;
            _baseVertices = authoredDynamicMesh.vertices;
            _baseNormals = authoredDynamicMesh.normals;
            _baseTangents = authoredDynamicMesh.tangents;
            if (_baseVertices.Length != authoredDynamicMesh.vertexCount ||
                _baseNormals.Length != _baseVertices.Length ||
                _baseTangents.Length != _baseVertices.Length)
            {
                throw new InvalidOperationException(
                    "The authored analog mesh requires position, normal and tangent data.");
            }

            _workingVertices = new List<Vector3>(_baseVertices);
            _workingNormals = new List<Vector3>(_baseNormals);
            _workingTangents = new List<Vector4>(_baseTangents);
        }

        public int VertexCount => _baseVertices.Length;

        public float GetLastDialAngleDegrees(AnalogInstrumentFunction function)
        {
            int index = (int)function;
            return index >= 0 && index < _lastDialAngles.Length
                ? _lastDialAngles[index]
                : 0f;
        }

        public void Apply(
            in CockpitSystemsSnapshot snapshot,
            Mesh destinationMesh,
            bool uploadToMesh)
        {
            for (int bindingIndex = 0;
                 bindingIndex < _bindings.Length;
                 bindingIndex++)
            {
                AnalogInstrumentMeshBinding binding = _bindings[bindingIndex];
                bool available =
                    AnalogInstrumentKinematics.TryEvaluateDialAngleDegrees(
                        binding.function,
                        snapshot,
                        out float dialAngle);
                if (!available) dialAngle = 0f;
                _lastDialAngles[(int)binding.function] = dialAngle;

                float rotationDegrees;
                if (available)
                {
                    float radians = dialAngle * Mathf.Deg2Rad;
                    Vector3 desiredDirection =
                        binding.dialRightLocal * Mathf.Cos(radians) +
                        binding.dialUpLocal * Mathf.Sin(radians);
                    rotationDegrees = Vector3.SignedAngle(
                        binding.authoredIndicatorDirectionLocal,
                        desiredDirection.normalized,
                        binding.rotationAxisLocal);
                }
                else
                {
                    rotationDegrees = 0f;
                }

                Quaternion rotation = Quaternion.AngleAxis(
                    rotationDegrees, binding.rotationAxisLocal);
                int end = binding.firstVertex + binding.vertexCount;
                for (int vertex = binding.firstVertex; vertex < end; vertex++)
                {
                    _workingVertices[vertex] =
                        binding.pivotLocal +
                        rotation * (_baseVertices[vertex] - binding.pivotLocal);
                    _workingNormals[vertex] =
                        rotation * _baseNormals[vertex];
                    Vector4 sourceTangent = _baseTangents[vertex];
                    Vector3 tangent = rotation * new Vector3(
                        sourceTangent.x,
                        sourceTangent.y,
                        sourceTangent.z);
                    _workingTangents[vertex] = new Vector4(
                        tangent.x, tangent.y, tangent.z, sourceTangent.w);
                }
            }

            if (!uploadToMesh) return;
            if (destinationMesh == null)
                throw new ArgumentNullException(nameof(destinationMesh));
            (motionHistory ??= new QuestFlight.Rendering.ProductDeformedMeshHistory()).Upload(destinationMesh,_workingVertices);
            destinationMesh.SetVertices(
                _workingVertices, 0, _workingVertices.Count, UploadFlags);
            destinationMesh.SetNormals(
                _workingNormals, 0, _workingNormals.Count, UploadFlags);
            destinationMesh.SetTangents(
                _workingTangents, 0, _workingTangents.Count, UploadFlags);
        }
    }
}
