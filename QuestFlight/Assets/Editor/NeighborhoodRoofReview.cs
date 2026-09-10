using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    public static partial class FullMapBuildingClipmapAssetAuthoring
    {
        [Serializable] public sealed class NeighborhoodRoofReviewData
        {
            public int id;
            public Vector3[] boundary,vertices;
            public int[] indices;
        }
        [Serializable] private sealed class NeighborhoodRoofReviewFile
        {
            public string schema;
            public NeighborhoodRoofReviewData[] buildings;
        }

        // Unsaved review meshes only. The shipping authoring entry points never
        // read this file, alter their allocation, or enable these replacements.
        public static Dictionary<string,Mesh> CreateNeighborhoodRoofReviewMeshes(string path)
        {
            var data=JsonUtility.FromJson<NeighborhoodRoofReviewFile>(File.ReadAllText(path));
            if(data.schema!="questflight.neighborhood-roof-review.v1" || data.buildings.Length==0)
                throw new InvalidDataException("Empty or invalid roof review");
            var overrides=data.buildings.ToDictionary(r=>r.id);
            foreach(var roof in overrides.Values)
                if(roof.boundary.Length<3 || roof.indices.Length%3!=0 || roof.indices.Any(i=>i<0||i>=roof.vertices.Length))
                    throw new InvalidDataException("Invalid reviewed roof: "+roof.id);
            Database db=LoadDatabase();
            var terrain=LoadRequired<ProductionTerrainSurfaceAsset>(TerrainSurfacePath);
            // Freeze existing allocation. No roof elsewhere can be displaced.
            RoofAllocation allocation=BuildRoofAllocation(db,new HashSet<int>());
            var result=new Dictionary<string,Mesh>();
            foreach(Page page in db.pages.Where(p=>p.buildings.Any(b=>overrides.ContainsKey(b.id))))
            {
                var builder=new MeshBuilder();
                foreach(Building building in page.buildings)
                {
                    Vector2[] source=Enumerable.Range(0,building.lod2_q.Length/2).Select(i=>new Vector2(
                        -12000+page.column*1000+building.lod2_q[i*2]*.1f,
                        -12000+page.row*1000+building.lod2_q[i*2+1]*.1f)).ToArray();
                    overrides.TryGetValue(building.id,out var roof);
                    if(roof!=null && allocation.IsSelected(building.id))
                        throw new InvalidOperationException("Review must preserve existing allocated roof: "+building.id);
                    AddBuilding(builder,CompileStableFootprint(source,building),building,terrain,true,true,allocation.IsSelected(building.id),roof);
                }
                string name=$"KBDU_buildings_stable_{page.column:D3}_{page.row:D3}";
                var mesh=new Mesh { name=name,hideFlags=HideFlags.DontSave,indexFormat=builder.vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16 };
                mesh.SetVertices(builder.vertices); mesh.SetNormals(builder.normals); mesh.SetTangents(builder.tangents);
                mesh.SetUVs(0,builder.detailUvs); mesh.SetUVs(1,builder.atlasCells); mesh.SetUVs(2,builder.parameters);
                mesh.SetColors(builder.colors); mesh.SetIndices(builder.indices,MeshTopology.Triangles,0,true);
                result.Add(name,mesh);
            }
            return result;
        }
    }
}
