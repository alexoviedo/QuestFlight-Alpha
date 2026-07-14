using NUnit.Framework;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlightLab.EditorTests
{
    public sealed class MetaXrSimulatorQaEditorTests
    {
        private const string AircraftPrefabPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";

        [Test]
        public void CgPivotCheckAllowsPresentationInterpolationButRejectsAuthoredPivotMutation()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AircraftPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                AircraftReferenceFrameRig rig = instance.GetComponent<AircraftReferenceFrameRig>();
                Assert.That(rig, Is.Not.Null);
                Assert.That(MetaXrSimulatorQaArchitectureChecks.UsesAuthoredCenterOfGravityPivot(rig), Is.True);

                rig.ApplyPresentationPoseForTest(
                    rig.AircraftSimulationRoot.position + new Vector3(0.8f, 0.15f, -0.4f),
                    rig.AircraftSimulationRoot.rotation * Quaternion.Euler(2f, 4f, -3f));
                Assert.That(rig.AircraftVisualRoot.localPosition.sqrMagnitude, Is.GreaterThan(0.01f),
                    "The test must exercise a real interpolated presentation offset.");
                Assert.That(MetaXrSimulatorQaArchitectureChecks.UsesAuthoredCenterOfGravityPivot(rig), Is.True,
                    "Transient render interpolation must not be confused with pivot ownership.");

                Vector3 authoredCg = rig.CenterOfGravityReference.localPosition;
                rig.CenterOfGravityReference.localPosition = new Vector3(0.1f, 0f, 0f);
                Assert.That(MetaXrSimulatorQaArchitectureChecks.UsesAuthoredCenterOfGravityPivot(rig), Is.False);
                rig.CenterOfGravityReference.localPosition = authoredCg;

                Transform authoredVisualParent = rig.AircraftVisualRoot.parent;
                GameObject wrongParent = new GameObject("WrongAircraftPivotParent");
                try
                {
                    wrongParent.transform.SetParent(authoredVisualParent, false);
                    rig.AircraftVisualRoot.SetParent(wrongParent.transform, true);
                    Assert.That(MetaXrSimulatorQaArchitectureChecks.UsesAuthoredCenterOfGravityPivot(rig), Is.False);
                }
                finally
                {
                    rig.AircraftVisualRoot.SetParent(authoredVisualParent, true);
                    Object.DestroyImmediate(wrongParent);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
