using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    /// <summary>Reject stale contact geometry before packaging a product build.</summary>
    public sealed class RoadSurfaceBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10;
        [Serializable] sealed class Sources
        {
            public string sourceManifestSha256,nearMeshSha256,authoritySha256;
            public string nearRoadSha256,outerRoadSha256,bankSha256,groundQuerySha256;
            public float maximumBakeProbeError;
            public int corridors,samples;
        }
        public void OnPreprocessBuild(BuildReport report) => Validate();
        public static void Validate()
        {
            const string root="Assets/Production/Environment/";
            var source=JsonUtility.FromJson<Sources>(File.ReadAllText(root+"TrafficV2/Data/KBDU_VisibleRoadLaneSurfaces_Source.json"));
            if(source==null||source.corridors!=32770||source.samples<=0||!float.IsFinite(source.maximumBakeProbeError)||source.maximumBakeProbeError>.02f)
                throw new BuildFailedException("Visible-road contact bake has not passed its 2 cm consistency gate.");
            Check(root+"TrafficV2/Data/KBDU_TrafficV2FullMapManifest.json",source.sourceManifestSha256);
            Check(root+"TrafficV2/Data/KBDU_VisibleRoadLaneSurfaces.bytes",source.bankSha256);
            Check(root+"PhotorealAerialSurfaceV1/Terrain/Native/KBDU_ProductAeqd_BareEarth_Fixed.asset",source.nearMeshSha256);
            Check(root+"GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json",source.authoritySha256);
            Check(root+"GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Near_V1.asset",source.nearRoadSha256);
            Check(root+"GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Outer_V1.asset",source.outerRoadSha256);
            Check(root+"ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset",source.groundQuerySha256);
        }
        static void Check(string path,string expected)
        {
            using var sha=SHA256.Create();
            string actual=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
            if(string.IsNullOrEmpty(expected)||actual!=expected)
                throw new BuildFailedException("Road support data is stale for "+path+". Re-run the approved offline road authoring pipeline.");
        }
    }
}
