using System;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class NeighborhoodFoliageReplacementTests
    {
        [Test] public void SerializedFoliageUsesExistingMaterialAndImportedAlphaAtlas()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FullMapBuildingClipmapAssetAuthoring.EnvironmentPath);
            var child=prefab.transform.Find("NeighborhoodFoliageV1");
            Assert.That(child,Is.Not.Null);
            var material=child.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material,Is.SameAs(AssetDatabase.LoadAssetAtPath<Material>(AirportCloseTreeLodAuthoring.CanopyMaterialPath)));
            Assert.That(material.GetTexture("_NeighborhoodFoliageAtlas"),Is.Not.Null);
            Assert.That(material.GetFloat("_SemanticMode"),Is.EqualTo(1));
            var mesh=child.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.GetIndexCount(0)/3,Is.LessThanOrEqualTo(10805));
            Assert.That(mesh.isReadable,Is.False);
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetTexture("_NeighborhoodFoliageAtlas")));
            Assert.That(importer.mipMapsPreserveCoverage,Is.True);
            Assert.That(importer.GetPlatformTextureSettings("Android").format,Is.EqualTo(TextureImporterFormat.ASTC_6x6));
        }

        [Test] public void ReplacementIsBoundToAuthoredCentersAndFallsBackWhenDisabled()
        {
            var root=new GameObject("Replacement test");
            try
            {
                var trees=root.AddComponent<AirportCloseTreeLodRenderer>();
                var renderer=root.AddComponent<MeshRenderer>();
                trees.ConfigureAuthoredFoliageReplacement(renderer,new[]{new Vector2(-801.2f,-710.3f)});
                Assert.That(trees.HasActiveAuthoredReplacementAt(new Vector3(-801.2f,50,-710.3f)),Is.True);
                Assert.That(trees.HasActiveAuthoredReplacementAt(new Vector3(-801.3f,50,-710.3f)),Is.False);
                renderer.enabled=false;
                Assert.That(trees.HasActiveAuthoredReplacementAt(new Vector3(-801.2f,50,-710.3f)),Is.False);
                renderer.enabled=true;
                trees.ConfigureAuthoredFoliageReplacement(null,Array.Empty<Vector2>());
                Assert.That(trees.HasActiveAuthoredReplacementAt(new Vector3(-801.2f,50,-710.3f)),Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void MissingRendererAndUnboundedReplacementsAreRejected()
        {
            var root=new GameObject("Replacement validation");
            try
            {
                var trees=root.AddComponent<AirportCloseTreeLodRenderer>();
                Assert.Throws<ArgumentException>(()=>trees.ConfigureAuthoredFoliageReplacement(null,new[]{Vector2.zero}));
                Assert.Throws<ArgumentException>(()=>trees.ConfigureAuthoredFoliageReplacement(null,new Vector2[257]));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
