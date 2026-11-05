using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static class SoaringYawStringAuthoring
    {
        public const string World = "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        public static void Repair()
        {
            string folder = SoaringAircraftAuthoring.Folder;
            var moving = AssetDatabase.LoadAssetAtPath<Mesh>(folder + "/SGS_Local_Moving.asset");
            var hull = AssetDatabase.LoadAssetAtPath<Mesh>(folder + "/SGS_Local_Static.asset");
            var canopy = AssetDatabase.LoadAssetAtPath<Mesh>(folder + "/SGS_Local_Canopy.asset");
            var root = PrefabUtility.LoadPrefabContents(World);
            SoaringMovingPart old;
            Vector3 eye;
            try
            {
                var p = root.GetComponentInChildren<SoaringInstrumentPresenter>(true);
                old = p.parts.Single(x => x.name == "YawString");
                eye = AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder + "/SGS233A_Reference.asset").eyeFromCgMeters;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if(old.firstVertex + old.vertexCount != moving.vertexCount) throw new Exception("Yaw string must be last authored moving range.");
            if(!IntersectCanopy(canopy,eye,new Vector3(0,.09f,.44f).normalized,out var hit,out var normal)) throw new Exception("Yaw string sightline missed canopy.");
            Vector3 anchor = hit + normal * .003f, aft = SoaringYawString.AftTangent(normal);
            Vector3 right = Vector3.Cross(aft,normal).normalized;
            int beforeTriangles = moving.triangles.Length/3, beforeVertices = moving.vertexCount;
            var vertices=moving.vertices.Take(old.firstVertex).ToList();
            var normals=moving.normals.Take(old.firstVertex).ToList();
            var colors=moving.colors.Take(old.firstVertex).ToList();
            var indices=new List<int>();
            var previous=moving.triangles;
            for(int i=0;i<previous.Length;i+=3)
                if(previous[i]<old.firstVertex && previous[i+1]<old.firstVertex && previous[i+2]<old.firstVertex)
                    indices.AddRange(new[]{previous[i],previous[i+1],previous[i+2]});
            for(int ring=0;ring<=SoaringYawString.Segments;ring++)
                for(int side=0;side<SoaringYawString.Sides;side++)
                {
                    float angle=side*Mathf.PI*2/SoaringYawString.Sides;
                    Vector3 radial=right*Mathf.Cos(angle)+normal*Mathf.Sin(angle);
                    float taper=Mathf.Lerp(1,.7f,(float)ring/SoaringYawString.Segments);
                    vertices.Add(anchor+aft*(SoaringYawString.Length*ring/SoaringYawString.Segments)+radial*(SoaringYawString.Radius*taper));
                    normals.Add(radial);colors.Add(new Color(.72f,.035f,.025f));
                }
            for(int ring=0;ring<SoaringYawString.Segments;ring++)
                for(int side=0;side<SoaringYawString.Sides;side++)
                {
                    int a=old.firstVertex+ring*3+side,b=old.firstVertex+ring*3+(side+1)%3,c=a+3,d=b+3;
                    // Check winding below against the intended outward normal.
                    foreach(var tri in new[]{new[]{a,c,d},new[]{a,d,b}})
                    {
                        if(Vector3.Dot(Vector3.Cross(vertices[tri[1]]-vertices[tri[0]],vertices[tri[2]]-vertices[tri[0]]),normals[tri[0]]+normals[tri[1]]+normals[tri[2]])<0)
                            (tri[1],tri[2])=(tri[2],tri[1]);
                        indices.AddRange(tri);
                    }
                }
            Bounds bounds=moving.bounds;
            moving.Clear();moving.SetVertices(vertices);moving.SetNormals(normals);moving.SetColors(colors);moving.SetTriangles(indices,0);moving.bounds=bounds;
            if(moving.triangles.Length/3!=beforeTriangles)throw new Exception("Frozen triangle budget changed.");
            // Relocate only the final 36 vertices (existing tape box), not any
            // canopy, panel, eye, instrument, or control geometry.
            var hv=hull.vertices;var hn=hull.normals;int start=hv.Length-36;
            Vector3 center=Vector3.zero;for(int i=start;i<hv.Length;i++)center+=hv[i];center/=36;
            Quaternion tapeRotation=Quaternion.LookRotation(normal,aft);
            // Idempotent on repaired input: its tape already has the target pose.
            if(old.vertexCount!=SoaringYawString.VertexCount)
            {
                for(int i=start;i<hv.Length;i++){hv[i]=hit+normal*.0008f+tapeRotation*(hv[i]-center);hn[i]=tapeRotation*hn[i];}
                hull.vertices=hv;hull.normals=hn;EditorUtility.SetDirty(hull);
            }
            EditorUtility.SetDirty(moving);
            foreach(string path in new[]{World,"Assets/Production/Prefabs/ProductionC172AircraftRig.prefab"})
            {
                root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var p=root.GetComponentInChildren<SoaringInstrumentPresenter>(true);
                    int index=Array.FindIndex(p.parts,x=>x.name=="YawString");
                    var part=p.parts[index];part.anchor=anchor;part.rotationAxis=normal;part.vertexCount=SoaringYawString.VertexCount;p.parts[index]=part;
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[YawString] authored anchor={anchor:F6} normal={normal:F6}; moving triangles {beforeTriangles}->{moving.triangles.Length/3}; vertices {beforeVertices}->{moving.vertexCount}; 0 new renderer/material/texture.");
        }

        public static bool IntersectCanopy(Mesh mesh,Vector3 origin,Vector3 direction,out Vector3 hit,out Vector3 normal)
        {
            var v=mesh.vertices;var t=mesh.triangles;float nearest=float.PositiveInfinity;hit=normal=Vector3.zero;
            for(int i=0;i<t.Length;i+=3)
            {
                Vector3 a=v[t[i]],e1=v[t[i+1]]-a,e2=v[t[i+2]]-a,p=Vector3.Cross(direction,e2);
                float determinant=Vector3.Dot(e1,p);if(Mathf.Abs(determinant)<1e-8f)continue;
                Vector3 delta=origin-a;float u=Vector3.Dot(delta,p)/determinant;if(u<0||u>1)continue;
                Vector3 q=Vector3.Cross(delta,e1);float w=Vector3.Dot(direction,q)/determinant;if(w<0||u+w>1)continue;
                float distance=Vector3.Dot(e2,q)/determinant;if(distance<=0||distance>=nearest)continue;
                nearest=distance;hit=origin+direction*distance;normal=Vector3.Cross(e1,e2).normalized;
                if(Vector3.Dot(normal,direction)<0)normal=-normal;
            }
            // At the centre seam use the symmetric canopy tangent, avoiding a
            // facet's left/right tilt creating a false yaw indication.
            if(!float.IsInfinity(nearest)){normal.x=0;normal.Normalize();return true;}return false;
        }
    }
}
