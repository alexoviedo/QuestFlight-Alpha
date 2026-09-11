using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    // QA only: called after MetaXrSimulatorQaBuild verifies its isolated project.
    // Android bundle headers cannot be loaded by a Windows development player.
    public static class MetaXrSimulatorSceneryBuild
    {
        [Serializable] private sealed class Page { public string asset_name,bundle_name,tier,sha256; public float[] local_bounds_meters; public int[] output_pixels; }
        [Serializable] private sealed class Pages { public Page[] pages; }
        [Serializable] private sealed class Bundle { public string name,hash; public uint crc; }
        [Serializable] private sealed class Index { public string contract_version,compiler_version; public Bundle[] bundles; }
        private static string Hash(string path)
        {
            using var stream=File.OpenRead(path);using var sha=SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        public static void BuildAirportCoverage()
        {
            string actual=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            string expected=System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_EXPECTED_PROJECT_ROOT");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.StandaloneWindows64 ||
                string.IsNullOrEmpty(expected) || !string.Equals(actual,Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase) ||
                System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_ALLOW_DEV_COPY")!="1")
                throw new InvalidOperationException("Simulator scenery may only be built in the authorized Windows QA copy");
            string source=System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_AERIAL_SOURCES");
            if(string.IsNullOrEmpty(source))throw new InvalidOperationException("QFL_META_XR_SIMULATOR_AERIAL_SOURCES must identify retained terrain_recovery/pages");
            const string aerial="Assets/StreamingAssets/QuestFlightAerialClipmap";
            var document=JsonUtility.FromJson<Pages>(File.ReadAllText(aerial+"/clipmap_manifest.json"));
            float westExtent=4000;
            string west=System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_WEST_EXTENT");
            if(!string.IsNullOrEmpty(west) && (!float.TryParse(west,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out westExtent) || westExtent<4000 || westExtent>8000))
                throw new InvalidDataException("Simulator west extent must be between 4000 and 8000 metres.");
            // Bounded airport inspection, not whole-map simulator acceptance.
            var names=new HashSet<string>(document.pages.Where(p=>p.tier=="global" ||
                p.local_bounds_meters[0]<4000 && p.local_bounds_meters[2]>-westExtent &&
                p.local_bounds_meters[1]<4000 && p.local_bounds_meters[3]>-4000).Select(p=>p.bundle_name));
            var pages=document.pages.Where(p=>names.Contains(p.bundle_name)).ToArray();
            const string generated="Assets/Generated/SimulatorAirportAerial";
            Directory.CreateDirectory(generated);
            var paths=new Dictionary<string,string>();
            foreach(var p in pages)
            {
                string input=Path.Combine(source,p.tier,p.asset_name+".jpg");
                if(Hash(input)!=p.sha256)
                {
                    // Retain the accepted compressed tile's decoded pixels for the
                    // previously documented, non-byte-identical reprojection case.
                    if(p.asset_name!="KBDU_2020_high_016_016")throw new InvalidDataException("Changed simulator source: "+input);
                    input=Path.Combine(Directory.GetParent(source).FullName,"accepted_decoded.png");
                    if(Hash(input)!="883bb103dac44f89c133ffa48f224b5a162c2bebc16f03bb00796a882fac38d8")
                        throw new InvalidDataException("Accepted decoded source checkpoint changed");
                }
                string destination=generated+"/"+p.asset_name+Path.GetExtension(input);
                File.Copy(input,destination,true);paths[p.asset_name]=destination;
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(var p in pages)
                {
                    var importer=(TextureImporter)AssetImporter.GetAtPath(paths[p.asset_name]);
                    importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
                    importer.mipmapEnabled=true;importer.isReadable=false;importer.wrapMode=TextureWrapMode.Clamp;
                    importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=8;importer.npotScale=TextureImporterNPOTScale.None;
                    importer.maxTextureSize=p.output_pixels[0];importer.textureCompression=TextureImporterCompression.CompressedHQ;
                    importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Standalone",overridden=true,
                        maxTextureSize=p.output_pixels[0],format=TextureImporterFormat.BC7,compressionQuality=100});
                    AssetDatabase.WriteImportSettingsIfDirty(paths[p.asset_name]);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var aerialBuilds=pages.GroupBy(p=>p.bundle_name).Select(g=>new AssetBundleBuild{
                assetBundleName=g.Key,assetNames=g.Select(p=>paths[p.asset_name]).ToArray(),
                addressableNames=g.Select(p=>p.asset_name).ToArray()}).ToArray();
            var options=BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode;
            if(BuildPipeline.BuildAssetBundles(aerial,aerialBuilds,options,BuildTarget.StandaloneWindows64)==null)
                throw new InvalidOperationException("Simulator aerial compilation failed");

            const string buildings="Assets/StreamingAssets/QuestFlightBuildingClipmap";
            var index=JsonUtility.FromJson<Index>(File.ReadAllText(buildings+"/bundle_index.json"));
            var builds=new List<AssetBundleBuild>();
            foreach(var bundle in index.bundles)
            {
                string[] parts=bundle.name.Split('_');int row=int.Parse(parts[2].Substring(1)),col=int.Parse(parts[3].Substring(1));
                var addresses=new List<string>();
                for(int y=row*2;y<row*2+2;y++)for(int x=col*2;x<col*2+2;x++)addresses.Add($"KBDU_buildings_stable_{x:D3}_{y:D3}");
                var assets=addresses.Select(a=>"Assets/Generated/FullMapBuildingClipmap/"+a+".asset").ToArray();
                foreach(string asset in assets)if(!File.Exists(asset))throw new FileNotFoundException("Simulator building source missing",asset);
                builds.Add(new AssetBundleBuild{assetBundleName=bundle.name,assetNames=assets,addressableNames=addresses.ToArray()});
            }
            var built=BuildPipeline.BuildAssetBundles(buildings,builds.ToArray(),options,BuildTarget.StandaloneWindows64);
            if(built==null)throw new InvalidOperationException("Simulator building compilation failed");
            foreach(var bundle in index.bundles)
            {
                if(!BuildPipeline.GetCRCForAssetBundle(buildings+"/"+bundle.name,out bundle.crc))throw new InvalidOperationException("Simulator bundle CRC unavailable");
                bundle.hash=built.GetAssetBundleHash(bundle.name).ToString();
            }
            File.WriteAllText(buildings+"/bundle_index.json",JsonUtility.ToJson(index,true));
            string report=System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_BUILD_REPORT");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(report),"simulator_scenery_scope.txt"),
                $"Windows QA only. Airport aerial coverage +/-4 km, expanded to bundle boundaries: {pages.Length} pages / {aerialBuilds.Length} bundles; {builds.Count} building bundles.\n"+
                "BC7 development textures; not Android ASTC fidelity or Quest timing evidence. The known 016_016 source exception uses the hash-verified accepted decoded texture.\n"+
                "Unconverted distant aerial bundles remain Android inputs: do not claim whole-map Windows coverage.\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
