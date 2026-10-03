#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    public static partial class CockpitPedalCompletionAuthoring
    {
        // The source has consumer-style floor hardware and an incomplete fourth
        // heel. Reuse its intact cast footplate and tread, not the incomplete base.
        // Floor bearings, transverse rudder bars and separate toe hinges follow
        // Cessna 172 service manual Figure 10-2; dimensions are visual approximations.
        private static Mesh BuildLinkageMesh(Mesh source,Mesh visibilityStream,Transform rendererRoot,Transform[] bones,
            out int sourceComponentCount,out int copiedComponents,out int rejectedComponents,out int rejectedTriangles)
        {
            var sv=source.vertices;var sn=source.normals;var st=source.tangents;
            var sc=ReadColors(source,visibilityStream);var su=ReadUvChannels(source);var si=source.triangles;
            var components=BuildTriangleComponents(sv,si);
            sourceComponentCount=components.Count;
            Require(sourceComponentCount==81,"Frozen pedal source component count changed.");
            int[] selected={0,35,36,37,38,39,40,48,49};
            Require(selected.Sum(i=>components[i].triangleStarts.Count)==3144,"Complete source footplate selection changed.");
            copiedComponents=selected.Length*4;rejectedComponents=0;rejectedTriangles=0;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var tangents=new List<Vector4>();
            var colors=new List<Color32>();var weights=new List<BoneWeight>();var triangles=new List<int>();
            var uvs=Enumerable.Range(0,8).Select(_=>new List<Vector4>()).ToArray();
            float[] centers={-.0345f,-.01338f,.01338f,.0345f};
            for(int pedal=0;pedal<4;pedal++)
            {
                var map=new Dictionary<int,int>();
                bool mirror=(pedal%2)==1;
                int Copy(int index)
                {
                    if(map.TryGetValue(index,out int existing))return existing;
                    int result=vertices.Count;map.Add(index,result);
                    Vector3 v=sv[index],n=sn[index];Vector4 t=st[index];Color32 c=sc[index];
                    float x=v.x+.0345f;
                    v.x=centers[pedal]+(mirror?-x:x);v.y-=PlateLift;
                    if(mirror){n.x=-n.x;t.x=-t.x;t.w=-t.w;c=new Color32(c.r,c.a,c.b,c.g);}
                    vertices.Add(v);normals.Add(n);tangents.Add(t);colors.Add(c);
                    weights.Add(RigidWeight((PedalBone)(2+pedal*2)));
                    for(int channel=0;channel<8;channel++)
                        if(su[channel].Count==sv.Length)uvs[channel].Add(su[channel][index]);
                    return result;
                }
                foreach(int component in selected)foreach(int start in components[component].triangleStarts)
                {triangles.Add(Copy(si[start]));triangles.Add(Copy(si[start+(mirror?2:1)]));triangles.Add(Copy(si[start+(mirror?1:2)]));}
            }
            // Use a neutral, painted source surface sample; no new material or texture.
            int sample=si[components[48].triangleStarts[0]];
            int Add(Vector3 v,Vector3 n,PedalBone bone)
            {
                int i=vertices.Count;vertices.Add(v);normals.Add(n);tangents.Add(new Vector4(1,0,0,1));
                colors.Add(sc[sample]);weights.Add(RigidWeight(bone));
                for(int channel=0;channel<8;channel++)if(su[channel].Count==sv.Length)uvs[channel].Add(su[channel][sample]);
                return i;
            }
            void Tri(Vector3 a,Vector3 b,Vector3 c,Vector3 n,PedalBone bone)
            {triangles.Add(Add(a,n,bone));triangles.Add(Add(b,n,bone));triangles.Add(Add(c,n,bone));}
            void Tube(Vector3 a,Vector3 b,float radius,PedalBone bone)
            {
                const int sides=10;
                Vector3 axis=(b-a).normalized;
                Vector3 u=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.right).normalized;
                Vector3 v=Vector3.Cross(axis,u);
                for(int j=0;j<sides;j++)
                {
                    float p=j*Mathf.PI*2/sides,q=(j+1)*Mathf.PI*2/sides;
                    Vector3 d0=(u*Mathf.Cos(p)+v*Mathf.Sin(p))*radius,d1=(u*Mathf.Cos(q)+v*Mathf.Sin(q))*radius;
                    Vector3 normal=(d0+d1).normalized;
                    Tri(a+d0,a+d1,b+d1,normal,bone);Tri(a+d0,b+d1,b+d0,normal,bone);
                    Tri(a,a+d1,a+d0,-axis,bone);Tri(b,b+d0,b+d1,axis,bone);
                }
            }
            void Block(Vector3 center,Vector3 size)
            {
                Vector3[] p=new Vector3[8];for(int i=0;i<8;i++)p[i]=center+Vector3.Scale(size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                int[] faces={0,4,6,2,1,3,7,5,0,1,5,4,2,6,7,3,0,2,3,1,4,5,7,6};
                for(int f=0;f<24;f+=4){var a=p[faces[f]];var b=p[faces[f+1]];var c=p[faces[f+2]];var d=p[faces[f+3]];var n=Vector3.Cross(b-a,c-a).normalized;Tri(a,b,c,n,PedalBone.Root);Tri(a,c,d,n,PedalBone.Root);}
            }
            for(int pedal=0;pedal<4;pedal++)
            {
                float x=centers[pedal],z=(pedal%2)==0?LeftBarZ:RightBarZ;
                var bone=(PedalBone)(1+pedal*2);
                Tube(new Vector3(x,PivotY,z),new Vector3(x,ToeY,ToeZ),.001f,bone);
                Tube(new Vector3(x-.0045f,ToeY,ToeZ),new Vector3(x+.0045f,ToeY,ToeZ),.0007f,bone);
            }
            foreach(bool left in new[]{true,false})
            {
                float z=left?LeftBarZ:RightBarZ;
                Tube(new Vector3(-.041f,PivotY,z),new Vector3(.041f,PivotY,z),.0009f,left?PedalBone.PilotLeftRudder:PedalBone.PilotRightRudder);
                foreach(float x in new[]{-.040f,.040f})Block(new Vector3(x,PivotY+.0006f,z),new Vector3(.003f,.003f,.0035f));
            }
            var mesh=new Mesh{name="C172_FourPedalAssembly",indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTangents(tangents);mesh.SetColors(colors);
            for(int channel=0;channel<8;channel++)if(uvs[channel].Count==vertices.Count)mesh.SetUVs(channel,uvs[channel]);
            mesh.SetTriangles(triangles,0,true);mesh.boneWeights=weights.ToArray();
            mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*rendererRoot.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            // Conservative skinned bounds include both rudder and toe travel.
            var bounds=mesh.bounds;bounds.Expand(.016f);mesh.bounds=bounds;
            return mesh;
        }
    }
}
#endif
