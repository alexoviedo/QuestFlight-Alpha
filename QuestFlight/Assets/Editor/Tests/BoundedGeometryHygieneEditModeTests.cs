#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class BoundedGeometryHygieneEditModeTests
    {
        [Test]
        public void TinyLinkageFacesUseExactUnitAxesAndDegenerateDimensionsAreRejected()
        {
            Assert.That(Vector3.Cross(new Vector3(.003f,0,0),new Vector3(0,.003f,0)).normalized,Is.EqualTo(Vector3.zero),
                "Negative control reproduces the installed Unity epsilon failure.");
            Vector3[] expected={Vector3.left,Vector3.right,Vector3.down,Vector3.up,Vector3.back,Vector3.forward};
            for(int face=0;face<6;face++)
            {
                Assert.That(CockpitPedalCompletionAuthoring.TryBlockFaceUnitNormal(new Vector3(.003f,.003f,.01f),face,out Vector3 normal),Is.True);
                Assert.That(normal,Is.EqualTo(expected[face]));Assert.That(normal.magnitude,Is.EqualTo(1));
            }
            Assert.That(CockpitPedalCompletionAuthoring.TryBlockFaceUnitNormal(new Vector3(0,.003f,.01f),0,out _),Is.False);
            Assert.That(CockpitPedalCompletionAuthoring.TryBlockFaceUnitNormal(new Vector3(float.NaN,.003f,.01f),0,out _),Is.False);
        }

        [Test]
        public void CurrentPedalBufferHasUsableNormalsAndPreservesSkinning()
        {
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Production/Cockpit/Pedals/C172_FourPedalAssembly.asset");
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh)),Is.EqualTo("0171a1c78c4130e4b9805deb209b2ecd"));
            Assert.That(mesh.vertexCount,Is.EqualTo(15440));Assert.That(mesh.triangles.Length/3,Is.EqualTo(13024));
            var normals=mesh.normals;var vertices=mesh.vertices;var indices=mesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                Vector3 cross=Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]);
                if(cross.sqrMagnitude<=1e-24f)continue;
                for(int j=0;j<3;j++)Assert.That(normals[indices[i+j]].sqrMagnitude,Is.GreaterThan(.99f));
            }
            Assert.That(mesh.boneWeights.Length,Is.EqualTo(mesh.vertexCount));Assert.That(mesh.bindposes.Length,Is.GreaterThan(0));
        }

        [TestCase(0,2544)] [TestCase(1,582)] [TestCase(2,422)]
        public void CurrentSuperCubLodsContainOnlyContributingTriangles(int lod,int expected)
        {
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/Production/Environment/AmbientAircraftV1/Models/SuperCub_LOD{lod}_Body.asset");
            Vector3[] vertices=mesh.vertices;int[] indices=mesh.triangles;
            Assert.That(indices.Length/3,Is.EqualTo(expected));Assert.That(mesh.subMeshCount,Is.EqualTo(1));
            for(int i=0;i<indices.Length;i+=3)
                Assert.That(Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]).sqrMagnitude,Is.GreaterThan(1e-24f));
        }
    }
}
#endif
