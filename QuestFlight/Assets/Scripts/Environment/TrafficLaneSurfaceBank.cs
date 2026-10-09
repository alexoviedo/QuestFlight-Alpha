using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>Offline sampled visible-road lanes. No geometry, hierarchy or physics creation.</summary>
    public sealed class TrafficLaneSurfaceBank
    {
        public const int Magic = 0x51465232;
        private readonly Lane[][] lanes;
        private sealed class Lane
        {
            public float[] distance;
            public Vector3[] position, up;
        }
        public int CorridorCount => lanes.Length;
        public bool HasCorridor(int corridor) => corridor>=0&&corridor<lanes.Length&&lanes[corridor].Length>0;
        public int LaneCount(int corridor, bool bidirectional) => lanes[corridor].Length / (bidirectional ? 2 : 1);
        public TrafficLaneSurfaceBank(TextAsset asset, int expectedCorridors, string expectedManifestSha256)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            using var input = new MemoryStream(asset.bytes, false);
            using var compressed = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new BinaryReader(compressed);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != 2)
                throw new InvalidDataException("Traffic lane surface header mismatch");
            int count = reader.ReadInt32();
            if (count != expectedCorridors) throw new InvalidDataException("Traffic lane surface corridor mismatch");
            if (reader.ReadString() != expectedManifestSha256)
                throw new InvalidDataException("Traffic lane surface source manifest is stale");
            lanes = new Lane[count][];
            for (int c = 0; c < count; c++)
            {
                int slots = reader.ReadInt32();
                if (slots < 0 || slots > 16) throw new InvalidDataException("Invalid authored lane count");
                lanes[c] = new Lane[slots];
                for (int l = 0; l < slots; l++)
                {
                    int n = reader.ReadInt32();
                    if (n < 2 || n > 100000) throw new InvalidDataException("Invalid authored lane samples");
                    var lane = new Lane { distance = new float[n], position = new Vector3[n], up = new Vector3[n] };
                    for (int i = 0; i < n; i++)
                    {
                        lane.distance[i] = reader.ReadSingle();
                        lane.position[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        lane.up[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        if(!float.IsFinite(lane.distance[i]) || !float.IsFinite(lane.position[i].sqrMagnitude) ||
                            !float.IsFinite(lane.up[i].sqrMagnitude) || (i>0&&lane.distance[i]<=lane.distance[i-1]))
                            throw new InvalidDataException("Invalid/non-monotonic authored road samples");
                    }
                    lanes[c][l] = lane;
                }
            }
        }

        public bool TryGetLaneLength(int corridor, bool bidirectional, int direction,
            int laneIndex, out float lengthMeters)
        {
            lengthMeters = 0f;
            if ((uint)corridor >= lanes.Length) return false;
            Lane[] choices = lanes[corridor];
            int perDirection = bidirectional ? choices.Length / 2 : choices.Length;
            int slot = laneIndex + (bidirectional ? direction * perDirection : 0);
            if ((uint)slot >= choices.Length) return false;
            float length = choices[slot].distance[choices[slot].distance.Length - 1];
            if (!float.IsFinite(length) || length <= 0f) return false;
            lengthMeters = length;
            return true;
        }

        public bool TrySample(int corridor, bool bidirectional, int direction, int laneIndex,
            float fraction, float offsetMeters, out Vector3 position, out Vector3 up)
        {
            position = default; up = Vector3.up;
            if ((uint)corridor >= lanes.Length) return false;
            var choices = lanes[corridor];
            int perDirection = bidirectional ? choices.Length / 2 : choices.Length;
            int slot = laneIndex + (bidirectional ? direction * perDirection : 0);
            if ((uint)slot >= choices.Length) return false;
            Lane lane = choices[slot];
            float distance = Mathf.Clamp(fraction * lane.distance[lane.distance.Length - 1] + offsetMeters,
                0f, lane.distance[lane.distance.Length - 1]);
            int lo = 0, hi = lane.distance.Length - 1;
            while (lo + 1 < hi)
            {
                int m = (lo + hi) >> 1;
                if (lane.distance[m] <= distance) lo = m; else hi = m;
            }
            float t = Mathf.InverseLerp(lane.distance[lo], lane.distance[hi], distance);
            position = Vector3.LerpUnclamped(lane.position[lo], lane.position[hi], t);
            up = Vector3.LerpUnclamped(lane.up[lo], lane.up[hi], t).normalized;
            return true;
        }
    }
}
