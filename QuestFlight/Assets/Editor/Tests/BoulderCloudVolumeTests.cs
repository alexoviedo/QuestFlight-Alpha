using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public class BoulderCloudVolumeTests
    {
        [Test]
        public void AtlasUnpackPreservesEveryVoxelAndOrientation()
        {
            var atlas = new Color32[512*256];
            for(int y=0;y<256;y++) for(int x=0;x<512;x++)
                atlas[x+512*y]=new Color32((byte)x,(byte)y,(byte)(x/64+8*(y/32)),255);
            var volume=BoulderCloudVolumeAuthoring.Unpack(atlas);
            for(int z=0;z<64;z++) for(int y=0;y<32;y++) for(int x=0;x<64;x++)
                Assert.AreEqual(atlas[x+z%8*64+512*(y+z/8*32)],volume[x+64*(y+32*z)]);
        }

        [Test]
        public void EmptyCellsNeverExcludeInterpolatedSourceDensity()
        {
            var texture = new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                ImageConversion.LoadImage(texture,File.ReadAllBytes(BoulderCloudVolumeAuthoring.Root+"KBDU_CloudDensitySlices_RGBA.png"));
                var volume=BoulderCloudVolumeAuthoring.Unpack(texture.GetPixels32());
                var occupancy=BoulderCloudVolumeAuthoring.BuildOccupancy(volume);
                var random=new System.Random(19473);
                int emptySamples=0;
                for(int sample=0;sample<100000;sample++)
                {
                    var c=new Vector3((float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble());
                    int cellX=Math.Min(15,(int)(c.x*16)),cellY=Math.Min(7,(int)(c.y*8)),cellZ=Math.Min(15,(int)(c.z*16));
                    if(occupancy[cellX+16*(cellY+8*cellZ)].r!=0)continue;
                    emptySamples++;
                    var p=Vector3.Scale(c,new Vector3(63,31,63));
                    int x=(int)p.x,y=(int)p.y,z=(int)p.z;
                    // Every interpolation contributor must remain below the body threshold
                    // for both channels and therefore every authored blend between them.
                    for(int dz=0;dz<2;dz++)for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
                    {
                        var v=volume[Math.Min(63,x+dx)+64*(Math.Min(31,y+dy)+32*Math.Min(63,z+dz))];
                        Assert.Less(v.r/255f,.07f); Assert.Less(v.g/255f,.07f);
                    }
                }
                Assert.Greater(emptySamples,1000,"Occupancy must actually identify useful empty space.");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void SerializedVolumesHaveBoundedStorageAndCorrectFiltering()
        {
            var density=AssetDatabase.LoadAssetAtPath<Texture3D>(BoulderCloudVolumeAuthoring.VolumePath);
            var occupancy=AssetDatabase.LoadAssetAtPath<Texture3D>(BoulderCloudVolumeAuthoring.OccupancyPath);
            Assert.NotNull(density); Assert.NotNull(occupancy);
            Assert.AreEqual(64,density.width); Assert.AreEqual(32,density.height); Assert.AreEqual(64,density.depth);
            Assert.AreEqual(FilterMode.Bilinear,density.filterMode); Assert.AreEqual(FilterMode.Point,occupancy.filterMode);
            Assert.AreEqual(TextureWrapMode.Clamp,density.wrapMode); Assert.AreEqual(1,density.mipmapCount);
            Assert.IsFalse(density.isReadable); Assert.IsFalse(occupancy.isReadable);
            var material=AssetDatabase.LoadAssetAtPath<Material>(BoulderCloudVolumeAuthoring.Root+"KBDU_BoulderCloudShell.mat");
            Assert.AreSame(density,material.GetTexture("_DensityVolume"));
            Assert.AreSame(occupancy,material.GetTexture("_CloudOccupancy"));
        }
    }
}
