using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;
namespace QuestFlight.Editor
{
    public static class LocalRecoveryAuthoring
    {
        public static void RunBatch()
        {
            try { Apply(); EditorApplication.Exit(0); }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Apply()
        {
            const string path=ProductFlightModesFoundationAuthoring.WorldPrefabPath;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var recovery=root.GetComponentsInChildren<ProductMapBoundaryRecovery>(true).Single();
                var flight=root.GetComponentsInChildren<FlightDynamicsCoordinator>(true).Single();
                var pages=root.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true).Single();
                var aerial=root.GetComponentsInChildren<AircraftCenteredAerialClipmapController>(true).Single();
                // Immutable envelopes of authored structures. Large terrain/water/sky surfaces and
                // aircraft-local visuals are not obstacles. Streamed buildings have a verified 53 m maximum.
                var obstacles=root.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r=>r.enabled && r.gameObject.activeInHierarchy && !r.transform.IsChildOf(flight.simulationRoot) && r.GetComponentInParent<AmbientAircraftActor>()==null)
                    .Select(r=>r.bounds).Where(b=>b.size.x<2000&&b.size.z<2000&&b.size.y>2&&b.size.y<500 &&
                        flight.terrainSurface.TrySample(b.center.x,b.center.z,out var ground)&&b.max.y>ground.heightMeters+50)
                    .ToArray();
                recovery.ConfigureLocalSafety(pages,aerial,obstacles);
                recovery.AuthoredModeCoordinator.AuthoredNavigation.ConfigureRecovery(recovery);
                EditorUtility.SetDirty(recovery.AuthoredModeCoordinator.AuthoredNavigation);
                PrefabUtility.RecordPrefabInstancePropertyModifications(recovery.AuthoredModeCoordinator.AuthoredNavigation);
                EditorUtility.SetDirty(recovery);
                PrefabUtility.RecordPrefabInstancePropertyModifications(recovery);
                PrefabUtility.SaveAsPrefabAsset(root,path);
                Debug.Log("[LocalRecovery] Authored local policy and "+obstacles.Length+" static obstacle envelopes.");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
        }
    }
}
