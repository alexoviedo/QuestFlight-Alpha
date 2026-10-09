#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Tests.EditMode
{
    public class AircraftRunwayBlobShadowEditModeTests
    {
        private const string WorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";

        [Test]
        public void WorldAuthorsExactlyOneBoundedSiblingShadow()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPrefabPath);
            Assert.That(asset, Is.Not.Null);
            AircraftRunwayBlobShadow[] shadows =
                asset.GetComponentsInChildren<AircraftRunwayBlobShadow>(true);
            Assert.That(shadows.Length, Is.EqualTo(1));
            AircraftRunwayBlobShadow shadow = shadows[0];
            Assert.That(shadow.transform.parent.name, Is.EqualTo("PresentationEffects"));
            Assert.That(shadow.AircraftSimulationRoot, Is.Not.Null);
            Assert.That(shadow.Environment, Is.Not.Null);
            Assert.That(shadow.transform.IsChildOf(shadow.AircraftSimulationRoot), Is.False);
            Assert.That(shadow.BlobRenderer, Is.SameAs(shadow.GetComponent<MeshRenderer>()));
            Assert.That(shadow.BlobRenderer.sharedMaterial.shader.name,
                Is.EqualTo("QuestFlightLab/Aircraft Runway Blob Shadow"));
            Assert.That(shadow.BlobRenderer.sharedMaterial.renderQueue,
                Is.LessThan((int)RenderQueue.Transparent));
            Assert.That(shadow.BlobRenderer.sharedMaterial.GetTag("RenderType", false),
                Is.EqualTo("Transparent"));
            Assert.That(shadow.BlobRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(shadow.BlobRenderer.receiveShadows, Is.False);
            Mesh mesh = shadow.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(2));
        }

        [Test]
        public void ShadowAppearsOnlyNearTheAuthoritativeRunway()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPrefabPath);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                AircraftRunwayBlobShadow shadow =
                    root.GetComponentInChildren<AircraftRunwayBlobShadow>(true);
                ProductionEnvironmentRoot environment = shadow.Environment;
                Transform aircraft = shadow.AircraftSimulationRoot;
                Vector3 midpoint = environment.transform.TransformPoint(
                    Vector3.Lerp(environment.runway08EndLocal, environment.runway26EndLocal, 0.5f));
                aircraft.position = midpoint + environment.transform.up * 1.5f;
                shadow.UpdateShadowPoseNow();
                Assert.That(shadow.BlobRenderer.enabled, Is.True);

                aircraft.position = midpoint + environment.transform.up * (shadow.FadeOutHeightMeters + 5f);
                shadow.UpdateShadowPoseNow();
                Assert.That(shadow.BlobRenderer.enabled, Is.False);

                Vector3 runwayAxis = environment.transform.TransformPoint(environment.runway26EndLocal) -
                                      environment.transform.TransformPoint(environment.runway08EndLocal);
                Vector3 runwayRight = Vector3.Cross(environment.transform.up, runwayAxis.normalized);
                aircraft.position = midpoint + runwayRight * (environment.runwayWidthMeters + 10f);
                shadow.UpdateShadowPoseNow();
                Assert.That(shadow.BlobRenderer.enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ShadowRuntimeTypeDoesNotConstructHierarchy()
        {
            string[] forbidden = { "AddComponent", "Instantiate", "CreatePrimitive" };
            string source = System.IO.File.ReadAllText(
                "Assets/Scripts/Environment/AircraftRunwayBlobShadow.cs");
            Assert.That(forbidden.Where(source.Contains), Is.Empty);
        }

        [TestCase(-8f)]
        [TestCase(-2f)]
        [TestCase(0f)]
        [TestCase(2f)]
        [TestCase(8f)]
        public void ShadowPreservesAircraftFootprintAcrossAuthoredGradedRunway(float lateral)
        {
            var root = PrefabUtility.LoadPrefabContents(WorldPrefabPath);
            try
            {
                var shadow = root.GetComponentInChildren<AircraftRunwayBlobShadow>(true);
                var environment = shadow.Environment;
                Vector3 start = environment.transform.TransformPoint(environment.runway08EndLocal);
                Vector3 end = environment.transform.TransformPoint(environment.runway26EndLocal);
                Vector3 forward = (end - start).normalized;
                Vector3 right = Vector3.Cross(environment.transform.up, forward).normalized;
                Vector3 up = Vector3.Cross(forward, right).normalized;
                foreach (float along in new[] { .1f, .5f, .9f })
                foreach (float heading in new[] { 1f, -1f })
                {
                    Vector3 footprint = Vector3.Lerp(start, end, along) + right * lateral;
                    shadow.AircraftSimulationRoot.SetPositionAndRotation(footprint + up * 1.5f, Quaternion.LookRotation(forward * heading, up));
                    shadow.UpdateShadowPoseNow();
                    Assert.That(shadow.BlobRenderer.enabled, Is.True);
                    Assert.That(Vector3.ProjectOnPlane(shadow.transform.position - footprint, up).magnitude, Is.LessThan(.01f));
                    Assert.That(Vector3.Dot(shadow.transform.up, up), Is.GreaterThan(.99999f));
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
#endif
