using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using QuestFlightLab.Aircraft.CockpitDynamics.Analog;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;

namespace QuestFlight.Editor
{
    public static partial class CockpitInstrumentV2Authoring
    {
        private static readonly int[] TrainerUnusedSeeds = { 2469, 2585, 2849, 2501, 2789, 2922 };
        private sealed class TrainerMove
        {
            public FaceFrame From, To;
            public Vector3 Apply(Vector3 p)
            {
                Vector3 d = p - From.Center;
                float scale = To.Radius / From.Radius;
                // Scale only the aperture footprint, retaining physical depth.
                return To.Center + From.Right * Vector3.Dot(d, From.Right) * scale +
                    From.Up * Vector3.Dot(d, From.Up) * scale + From.Normal * Vector3.Dot(d, From.Normal);
            }
        }

        public static void BuildCompleteTrainer()
        {
            // Rebuild the immutable source partition before applying the layout.
            // This prevents cumulative translation on repeated authoring runs.
            BuildAssetsOnly();
            CockpitTrainerFinishAuthoring.Build();
            var source = AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(CockpitAnalogInstrumentAuthoring.LayoutAssetPath);
            var frozen = AssetDatabase.LoadAllAssetsAtPath(CockpitInstrumentV2EditorContract.SourceGlbPath).OfType<Mesh>().Single(m => m.name == CockpitInstrumentV2EditorContract.SourceMeshName);
            var components = BuildComponents(frozen);
            Vector3 right = source.Bindings[0].dialRightLocal.normalized;
            Vector3 up = source.Bindings[0].dialUpLocal.normalized;
            Vector3 normal = source.Bindings[0].rotationAxisLocal.normalized;
            FaceFrame Face(int seed) => Frame(frozen, components[seed], right, up, normal);
            var layout = AssetDatabase.LoadAssetAtPath<CockpitInstrumentV2Layout>(LayoutPath);
            var attitude = layout.AttitudeContract;
            Vector3 attitudeCenter = attitude.pivotLocal + normal * Vector3.Dot(Face(2541).Center - attitude.pivotLocal, normal);
            var attitudeFrame = new FaceFrame(-1, attitudeCenter, normal, right, up, .00365f);
            var moves = new[]
            {
                new TrainerMove { From = Face(2819), To = Face(2469) },
                new TrainerMove { From = attitudeFrame, To = new FaceFrame(2585, Face(2585).Center, normal, right, up, attitudeFrame.Radius) },
                new TrainerMove { From = Face(2667), To = Face(2849) },
                new TrainerMove { From = Face(2891), To = Face(2667) },
                new TrainerMove { From = Face(2541), To = Face(2891) }
            };
            FaceFrame[] removed = TrainerUnusedSeeds.Select(Face).ToArray();
            // One replacement disc closes every abandoned/decorative aperture.
            FaceFrame[] blanks = new[] { Face(2819), attitudeFrame, Face(2541), Face(2501), Face(2789), Face(2922) };
            Mesh neutral = layout.NeutralizedSourceMesh;
            TransformTrainerMesh(neutral, Matrix4x4.identity, moves, removed, true);
            AppendTrainerBlankingPlates(neutral, blanks);
            TransformTrainerMesh(layout.CombinedDynamicMesh, Matrix4x4.identity, moves, Array.Empty<FaceFrame>(), false);
            var bindings = layout.Bindings.ToArray();
            for (int i = 0; i < bindings.Length; i++)
            {
                var move = moves.FirstOrDefault(m => m.From.Seed == bindings[i].sourceTriangleSeed);
                if (move == null) continue;
                bindings[i].pivotLocal = move.Apply(bindings[i].pivotLocal);
                bindings[i].translationMetersPerUnit *= move.To.Radius / move.From.Radius;
            }
            attitude.pivotLocal = moves[1].Apply(attitude.pivotLocal);
            // Nonuniform footprint scaling would distort the gyro ball; use a
            // translation-only attitude move to retain its audited spherical fit.
            // The destination aperture is within 5% of its original diameter.
            layout.ConfigureGenerated(neutral, layout.CombinedDynamicMesh, layout.ExistingSourceMaterial, bindings,
                layout.SourceTriangleCount, neutral.triangles.Length / 3, layout.CombinedDynamicMesh.triangles.Length / 3,
                layout.SourceAssetGuid, layout.SourceSha256, layout.StaticOverlayVertexCount,
                true, layout.AltitudeDigitSlots, layout.AltitudeDigitUvRects);
            layout.ConfigureAttitudeContract(attitude);
            EditorUtility.SetDirty(layout);
            Material instrumentMaterial = layout.ExistingSourceMaterial;
            instrumentMaterial.SetVector("_AttitudePivot", attitude.pivotLocal);
            EditorUtility.SetDirty(instrumentMaterial);

            GameObject rig = PrefabUtility.LoadPrefabContents(CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                Transform meter = rig.transform.Find(CockpitAnalogInstrumentAuthoring.MeterRendererPath);
                // Recreate lens source in a temporary prefab instance from the
                // frozen geometry, so each run starts from the same 13 apertures.
                RebuildTrainerLens(rig, meter, frozen, moves, removed);
                // Adjustment controls are separate imported mesh islands.
                // Move their complete geometry, never flight-control pivots.
                foreach (MeshFilter filter in rig.GetComponentsInChildren<MeshFilter>(true))
                {
                    string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                    if (path.Contains("/Meshes/Trainer_"))
                    {
                        string originalName = Path.GetFileNameWithoutExtension(path).Substring("Trainer_".Length);
                        filter.sharedMesh = AssetDatabase.LoadAllAssetsAtPath(CockpitInstrumentV2EditorContract.SourceGlbPath).OfType<Mesh>().Single(m => m.name == originalName);
                        path = CockpitInstrumentV2EditorContract.SourceGlbPath;
                    }
                    string n = filter.name;
                    if (!path.Equals(CockpitInstrumentV2EditorContract.SourceGlbPath, StringComparison.Ordinal) ||
                        n.Contains("Meter") || n.Contains("ButtonPlate") || n.Contains("Interior_Body") || n.Contains("Steering") ||
                        n.Contains("Display") || n.Contains("Paddle") || n.Contains("Glass") || n.Contains("Exterior")) continue;
                    Matrix4x4 toMeter = meter.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    if (!filter.sharedMesh.vertices.Any(v => FindTrainerMove(toMeter.MultiplyPoint3x4(v), moves) != null)) continue;
                    Mesh copy = UnityEngine.Object.Instantiate(filter.sharedMesh);
                    copy.name = filter.sharedMesh.name;
                    if (n.Contains("Buttons"))
                    {
                        // Each adjuster is offset below its dial. Classify the
                        // complete island against all apertures, including the
                        // stationary altimeter and compass, before moving it.
                        var frames = new[] { 2469, 2585, 2713, 2819, 2849, 2667, 2891, 2541, 2789, 2501, 2621, 2759, 2906, 2922, 2339 }.Select(Face).Append(attitudeFrame).ToArray();
                        Vector3[] points = copy.vertices;
                        foreach (Component part in BuildComponents(copy).Values)
                        {
                            Vector3 center = part.Vertices.Select(i => toMeter.MultiplyPoint3x4(points[i])).Aggregate(Vector3.zero, (a,b) => a+b) / part.Vertices.Count;
                            FaceFrame nearest = frames.OrderBy(f => (center-f.Center).sqrMagnitude).First();
                            TrainerMove move = moves.FirstOrDefault(m => m.From.Seed == nearest.Seed);
                            if(move == null || !InTrainerAperture(center, nearest, 1.8f)) continue;
                            foreach(int i in part.Vertices) points[i] = toMeter.inverse.MultiplyPoint3x4(move.Apply(toMeter.MultiplyPoint3x4(points[i])));
                        }
                        copy.vertices=points; copy.RecalculateBounds();
                    }
                    else TransformTrainerMesh(copy, toMeter, moves, Array.Empty<FaceFrame>(), false);
                    string meshPath = "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Meshes/Trainer_" + filter.sharedMesh.name + ".asset";
                    filter.sharedMesh = SaveTrainerMesh(copy, meshPath);
                }
                PrefabUtility.SaveAsPrefabAsset(rig, CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(rig); }
            AssetDatabase.SaveAssets();
            AlignTrainerAttitudeAssembly();
            Require(layout.Validate(out string error), error);
            ValidateTrainerNeedles(layout, right, up);
            CockpitDepthOwnershipEditorContract.ValidateProductPrefab();
            Debug.Log("TRAINER COMPLETE: six-pack, closed unused apertures, matched IAS/VSI artwork and live calibration.");
        }

        private static void ValidateTrainerNeedles(CockpitInstrumentV2Layout layout, Vector3 right, Vector3 up)
        {
            foreach(var binding in layout.Bindings.Where(b => b.rotationFunction == CockpitInstrumentV2Function.IndicatedAirspeedNeedleDegrees || b.rotationFunction == CockpitInstrumentV2Function.IndicatedVerticalSpeedNeedleDegrees))
            {
                Vector3[] points=layout.CombinedDynamicMesh.vertices;
                Vector3 tip=points.Skip(binding.firstVertex).Take(binding.vertexCount).Select(v=>v-binding.pivotLocal).OrderByDescending(v=>v.sqrMagnitude).First();
                foreach(float value in new[]{-2000f,0f,100f,1000f,2000f})
                {
                    float angle=binding.rotationFunction == CockpitInstrumentV2Function.IndicatedAirspeedNeedleDegrees ? CockpitInstrumentV2Kinematics.EvaluateTrainerAirspeedAngle(value) : CockpitInstrumentV2Kinematics.EvaluateTrainerVerticalSpeedAngle(value);
                    Vector3 moved=Quaternion.AngleAxis((angle-binding.authoredRotationValue)*binding.rotationDegreesPerUnit,binding.rotationAxisLocal)*tip;
                    float actual=Mathf.Atan2(Vector3.Dot(moved,up),Vector3.Dot(moved,right))*Mathf.Rad2Deg;
                    Require(Mathf.Abs(Mathf.DeltaAngle(actual,angle))<2f,"Trainer needle/artwork registration failed: "+binding.rotationFunction+" "+actual+" vs "+angle);
                }
            }
            Debug.Log("TRAINER needle/artwork registration passed at rest, climb/descent and scale limits.");
        }

        private static TrainerMove FindTrainerMove(Vector3 p, TrainerMove[] moves)
        {
            return moves.Where(m => InTrainerAperture(p, m.From, 1.18f))
                .OrderBy(m => (p - m.From.Center).sqrMagnitude).FirstOrDefault();
        }

        private static bool InTrainerAperture(Vector3 p, FaceFrame f, float radial)
        {
            Vector3 d = p - f.Center;
            float x = Vector3.Dot(d, f.Right), y = Vector3.Dot(d, f.Up);
            return x * x + y * y < f.Radius * f.Radius * radial * radial && Mathf.Abs(Vector3.Dot(d, f.Normal)) < f.Radius * 1.8f;
        }

        private static void TransformTrainerMesh(Mesh mesh, Matrix4x4 toMeter, TrainerMove[] moves, FaceFrame[] removed, bool removeFaces)
        {
            Vector3[] v = mesh.vertices;
            // Remove obsolete surfaces instead of hiding them behind overlays.
            if (removeFaces)
            {
                int[] source = mesh.triangles;
                var retained = new List<int>();
                for (int t = 0; t < source.Length; t += 3)
                {
                    Vector3 center = toMeter.MultiplyPoint3x4((v[source[t]] + v[source[t+1]] + v[source[t+2]]) / 3f);
                    if (removed.Any(f => InTrainerAperture(center, f, 1.10f))) continue;
                    retained.Add(source[t]); retained.Add(source[t+1]); retained.Add(source[t+2]);
                }
                mesh.triangles = retained.ToArray();
            }
            Matrix4x4 inverse = toMeter.inverse;
            var masks = new List<Vector4>(); mesh.GetUVs(3, masks);
            TrainerMove gyroMove = moves.FirstOrDefault(m => m.From.Seed == -1);
            foreach (Component component in BuildComponents(mesh).Values)
            {
                if(gyroMove != null && masks.Count == v.Length && component.Vertices.Any(i => masks[i].z > .5f))
                {
                    foreach(int i in component.Vertices)v[i]=inverse.MultiplyPoint3x4(gyroMove.Apply(toMeter.MultiplyPoint3x4(v[i])));
                    continue;
                }
                Vector3 center = Vector3.zero;
                foreach(int i in component.Vertices)center += toMeter.MultiplyPoint3x4(v[i]);
                center /= component.Vertices.Count;
                TrainerMove move = FindTrainerMove(center, moves);
                if (move == null) continue;
                // Never tear a connected part across apertures: a case, knob,
                // pointer or triangle moves as a whole, or remains untouched.
                bool fits = component.Vertices.All(i => InTrainerAperture(toMeter.MultiplyPoint3x4(v[i]), move.From, 1.65f));
                if (!fits) continue;
                foreach(int i in component.Vertices)v[i] = inverse.MultiplyPoint3x4(move.Apply(toMeter.MultiplyPoint3x4(v[i])));
            }
            mesh.vertices = v;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }

        private static void AppendTrainerBlankingPlates(Mesh mesh, FaceFrame[] faces)
        {
            var v = mesh.vertices.ToList(); var n = mesh.normals.ToList(); var t = mesh.tangents.ToList();
            var uv = mesh.uv.ToList(); var selector = new List<Vector4>(); mesh.GetUVs(3, selector);
            var triangles = mesh.triangles.ToList();
            foreach (var face in faces)
            {
                var av = new List<Vector3>(); var an = new List<Vector3>(); var at = new List<Vector4>();
                var auv = new List<Vector2>(); var asel = new List<Vector2>(); var ai = new List<int>();
                bool leftCover = face.Seed == -1 || face.Seed == 2922;
                FaceFrame fitted = leftCover ? MeasuredLeftBlankingFrame(face.Seed) : face;
                AddDiscScaled(fitted, TrainerBlankTile, 0f, leftCover ? 1f : 1.07f, av, an, at, auv, asel, ai);
                int first = v.Count; v.AddRange(av); n.AddRange(an); t.AddRange(at); uv.AddRange(auv);
                selector.AddRange(asel.Select(s => new Vector4(1, 3, 0, 0)));
                triangles.AddRange(ai.Select(i => i + first));
            }
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTangents(t); mesh.SetUVs(0, uv); mesh.SetUVs(3, selector);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        }

        private static FaceFrame MeasuredLeftBlankingFrame(int seed)
        {
            // Measured from the frozen GLB ButtonPlate bezel loops (vertex
            // islands 10562 and 11730), in the shared meter-local coordinates.
            // The bezel planes differ from the source dial plane: fitting to
            // the gyro pivot/another dial tilted the former backing discs.
            bool large = seed == -1;
            Vector3 normal = (large
                ? new Vector3(0f, .1562557f, -.9877166f)
                : new Vector3(.0070168f, .1664330f, -.9860278f)).normalized;
            Vector3 center = large
                ? new Vector3(-.03133798f, -.01317921f, -.06882497f)
                : new Vector3(-.04177069f, -.01200912f, -.06872629f);
            // Recess beneath the existing rim. Radius remains within its outer
            // edge (0.003948 / 0.001968), overlapping the inner opening.
            center += normal * (large ? .00006f : .00003f);
            Vector3 right = Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
            Vector3 up = Vector3.Cross(normal, right).normalized;
            return new FaceFrame(seed, center, normal, right, up, large ? .00390f : .001945f);
        }

        public static void SealLeftTrainerBlankingPlates()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(NeutralizedSourceMeshPath);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            int discVertices = SegmentCount * 3;
            int first = vertices.Length - 6 * discVertices;
            var selectors = new List<Vector4>(); mesh.GetUVs(3, selectors);
            Require(first >= 0 && selectors.Count == vertices.Length,
                "Expected authored trainer blanking plates.");
            foreach (int slot in new[] { 1, 5 })
            {
                int start = first + slot * discVertices;
                Require(selectors.Skip(start).Take(discVertices).All(s => s.x == 1 && s.y == 3),
                    "Unexpected blanking plate vertex ownership.");
                Require(Mathf.Abs(vertices[start].x - (slot == 1 ? -.0313f : -.04175f)) < .001f,
                    "Unexpected left blanking plate location.");
                var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<Vector4>();
                var uv = new List<Vector2>(); var selector = new List<Vector2>(); var indices = new List<int>();
                AddDiscScaled(MeasuredLeftBlankingFrame(slot == 1 ? -1 : 2922), TrainerBlankTile,
                    0f, 1f, v, n, t, uv, selector, indices);
                Require(v.Count == discVertices, "Backing disc topology changed.");
                for(int i=0;i<discVertices;i++)
                {
                    vertices[start+i]=v[i]; normals[start+i]=n[i]; tangents[start+i]=t[i];
                }
            }
            mesh.vertices=vertices; mesh.normals=normals; mesh.tangents=tangents;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            Debug.Log("Fitted two left blanking covers to measured bezel planes/radii; topology and materials unchanged.");
        }

        public static void AlignTrainerAttitudeAssembly()
        {
            var layout = AssetDatabase.LoadAssetAtPath<CockpitInstrumentV2Layout>(LayoutPath);
            Mesh mesh = layout.NeutralizedSourceMesh;
            Mesh frozen = AssetDatabase.LoadAllAssetsAtPath(CockpitInstrumentV2EditorContract.SourceGlbPath)
                .OfType<Mesh>().Single(m => m.name == CockpitInstrumentV2EditorContract.SourceMeshName);
            var components = BuildComponents(frozen);
            int[] sourceTriangles = frozen.triangles;
            int[] wingVertices = new[] { 44,45,54,55 }.SelectMany(t => sourceTriangles.Skip(t*3).Take(3)).Distinct().ToArray();
            Vector3[] vertices=mesh.vertices, normals=mesh.normals;
            Vector4[] tangents=mesh.tangents;
            Vector3 reference=wingVertices.Select(i=>vertices[i]).Aggregate(Vector3.zero,(a,b)=>a+b)/wingVertices.Length;
            Vector3 destinationNormal=new Vector3(0,.15209878f,-.98836530f).normalized;
            Vector3 bezelCenter=new Vector3(-.02250458f,-.03164418f,-.07159216f);
            Vector3 destination=bezelCenter-destinationNormal*.00004f;
            // The fixed reference is already accepted. Center it independently;
            // never transfer its depth correction to the spherical display.
            var fixedVertices=new HashSet<int>(sourceTriangles.Take(70*3));
            foreach(int i in fixedVertices) vertices[i]+=destination-reference;

            var ballVertices=new HashSet<int>();
            foreach(int seed in CockpitInstrumentV2EditorContract.AttitudeSphereComponentSeeds)
                ballVertices.UnionWith(components[seed].Vertices);
            Require(ballVertices.Count==998 && fixedVertices.Count==140,"Unexpected attitude assembly ownership.");
            Vector3[] sourceVertices=frozen.vertices, sourceNormals=frozen.normals;
            Vector4[] sourceTangents=frozen.tangents;
            SphereFit fit=FitSphere(sourceVertices,ballVertices);
            int t0=44*3;
            // Normalize edges BEFORE crossing: source-unit triangle areas are
            // below Unity Vector3.normalized's small-vector cutoff.
            Vector3 sourceNormal=Vector3.Cross(
                (sourceVertices[sourceTriangles[t0+1]]-sourceVertices[sourceTriangles[t0]]).normalized,
                (sourceVertices[sourceTriangles[t0+2]]-sourceVertices[sourceTriangles[t0]]).normalized).normalized;
            if(sourceNormal.z>0)sourceNormal=-sourceNormal;
            Require(sourceNormal.sqrMagnitude>.99f,"Degenerate attitude reference plane.");
            Quaternion ballRotation=Quaternion.FromToRotation(sourceNormal,destinationNormal);
            // Preserve the pre-regression ball-to-bezel depth, while centering
            // its axis on the aperture rather than on the protruding symbol.
            Vector3 priorPivot=new Vector3(-.022550516f,-.031388126f,-.074853994f);
            float depth=Vector3.Dot(priorPivot-bezelCenter,destinationNormal);
            Vector3 pivot=bezelCenter+destinationNormal*depth;
            foreach(int i in ballVertices)
            {
                vertices[i]=pivot+ballRotation*(sourceVertices[i]-fit.Center);
                normals[i]=ballRotation*sourceNormals[i];
                Vector3 tangent=ballRotation*new Vector3(sourceTangents[i].x,sourceTangents[i].y,sourceTangents[i].z);
                tangents[i]=new Vector4(tangent.x,tangent.y,tangent.z,sourceTangents[i].w);
            }
            // The symbol is mounted in front of the moving sphere, not in the
            // bezel plane. Retain its accepted in-plane center while restoring
            // enough forward clearance for every pitch/bank orientation.
            float symbolBack=fixedVertices.Max(i=>Vector3.Dot(vertices[i]-bezelCenter,destinationNormal));
            float clearance=fit.MaximumError+.00002f;
            float symbolShift=depth-fit.Radius-clearance-symbolBack;
            foreach(int i in fixedVertices)vertices[i]+=destinationNormal*symbolShift;
            Require(fixedVertices.Max(i=>Vector3.Dot(vertices[i]-pivot,destinationNormal)) < -fit.Radius,
                "Fixed attitude reference must remain in front of the rotating ball.");
            mesh.vertices=vertices; mesh.normals=normals; mesh.tangents=tangents; mesh.RecalculateBounds();
            var contract=layout.AttitudeContract;
            contract.pivotLocal=pivot;
            contract.pitchAxisLocal=Vector3.right;
            contract.bankAxisLocal=destinationNormal;
            layout.ConfigureAttitudeContract(contract);
            layout.ExistingSourceMaterial.SetVector("_AttitudePivot",contract.pivotLocal);
            EditorUtility.SetDirty(mesh); EditorUtility.SetDirty(layout); EditorUtility.SetDirty(layout.ExistingSourceMaterial);
            AssetDatabase.SaveAssets();
            Debug.Log("Attitude ball independently fitted: depth="+depth+"; rotation="+Quaternion.Angle(Quaternion.identity,ballRotation)+" degrees; fixed reference and native pitch/roll preserved.");
        }

        private static Mesh SaveTrainerMesh(Mesh generated, string path)
        {
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
            EditorUtility.CopySerialized(generated, saved); UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(saved); return saved;
        }

        private static void RebuildTrainerLens(GameObject rig, Transform meter, Mesh frozen, TrainerMove[] moves, FaceFrame[] removed)
        {
            var visual = rig.GetComponent<QuestFlightLab.Runtime.AircraftReferenceFrameRig>().AircraftVisualRoot;
            var lens = rig.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "ProductionPrincipalInstrumentLensArray");
            // The reflection authoring helper needs frozen face topology only.
            MeshFilter meterFilter = meter.GetComponent<MeshFilter>(); Mesh existing = meterFilter.sharedMesh;
            meterFilter.sharedMesh = frozen;
            try
            {
                var helper = typeof(QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor.PhotorealAerialReflectionAuthoring).GetMethod("BuildInstrumentLensArray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                helper.Invoke(null, new object[] { visual, meter.GetComponent<Renderer>(), rig.GetComponentInChildren<Camera>(true).transform, lens.GetComponent<Renderer>().sharedMaterial });
            }
            finally { meterFilter.sharedMesh = existing; }
            TransformTrainerMesh(lens.sharedMesh, meter.worldToLocalMatrix * lens.transform.localToWorldMatrix, moves, removed, true);
        }

        private static void DrawTrainerFaces(Color32[] pixels)
        {
            foreach (int tile in new[] { TrainerAirspeedTile, TrainerVsiTile, TrainerBlankTile })
            {
                int ox = tile % TileColumns * TileSize, oy = tile / TileColumns * TileSize;
                for (int y=0;y<TileSize;y++) for(int x=0;x<TileSize;x++)
                {
                    float radius = new Vector2(x-128,y-128).magnitude;
                    byte c = (byte)(tile == TrainerBlankTile ? 27 : 12);
                    if(radius>116) c=(byte)Mathf.Lerp(c, 3, Mathf.InverseLerp(116,126,radius));
                    pixels[(oy+y)*AtlasWidth+ox+x]=new Color32(c,c,c,255);
                }
            }
            Color32 ink = new Color32(226,224,214,255);
            void Mark(int tile, float angle, string label, bool major)
            {
                int cx=tile%TileColumns*TileSize+128,cy=tile/TileColumns*TileSize+128;
                float a=angle*Mathf.Deg2Rad; Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                DrawLine(pixels,cx+Mathf.RoundToInt(d.x*(major?99:107)),cy+Mathf.RoundToInt(d.y*(major?99:107)),cx+Mathf.RoundToInt(d.x*115),cy+Mathf.RoundToInt(d.y*115),ink,major?2:1);
                if(!string.IsNullOrEmpty(label))DrawTextCentered(pixels,cx+Mathf.RoundToInt(d.x*81),cy+Mathf.RoundToInt(d.y*81),label,2,ink);
            }
            for(int speed=0;speed<=180;speed+=5)Mark(TrainerAirspeedTile,CockpitInstrumentV2Kinematics.EvaluateTrainerAirspeedAngle(speed),speed>=20 && speed%20==0?speed.ToString():null,speed%10==0);
            void Arc(float start,float end,int radius,Color32 color)
            {
                int cx=TrainerAirspeedTile%TileColumns*TileSize+128,cy=TrainerAirspeedTile/TileColumns*TileSize+128;
                for(float speed=start;speed<end;speed+=.25f)
                {
                    float a=CockpitInstrumentV2Kinematics.EvaluateTrainerAirspeedAngle(speed)*Mathf.Deg2Rad;
                    float b=CockpitInstrumentV2Kinematics.EvaluateTrainerAirspeedAngle(Mathf.Min(end,speed+.25f))*Mathf.Deg2Rad;
                    DrawLine(pixels,cx+Mathf.RoundToInt(Mathf.Cos(a)*radius),cy+Mathf.RoundToInt(Mathf.Sin(a)*radius),cx+Mathf.RoundToInt(Mathf.Cos(b)*radius),cy+Mathf.RoundToInt(Mathf.Sin(b)*radius),color,2);
                }
            }
            // C172P POH figure 2-2, indicated airspeed (not calibrated speed).
            Arc(33,85,94,ink); Arc(44,127,120,new Color32(73,151,80,255));
            Arc(127,158,120,new Color32(224,184,54,255)); Arc(157.5f,158.5f,119,new Color32(220,55,38,255));
            int acx=TrainerAirspeedTile%TileColumns*TileSize+128,acy=TrainerAirspeedTile/TileColumns*TileSize+128;
            DrawTextCentered(pixels,acx,acy+27,"AIRSPEED",2,ink); DrawTextCentered(pixels,acx,acy-35,"KNOTS",2,ink);
            for(int climb=-2000;climb<=2000;climb+=100)Mark(TrainerVsiTile,CockpitInstrumentV2Kinematics.EvaluateTrainerVerticalSpeedAngle(climb),climb%500==0?(Math.Abs(climb)/100).ToString():null,climb%500==0);
            int vcx=TrainerVsiTile%TileColumns*TileSize+128,vcy=TrainerVsiTile/TileColumns*TileSize+128;
            DrawTextCentered(pixels,vcx,vcy+25,"VERTICAL",2,ink); DrawTextCentered(pixels,vcx,vcy+7,"SPEED",2,ink);
            DrawTextCentered(pixels,vcx,vcy-33,"100 FT/MIN",1,ink);
        }
        public static void AuditTrainerPanel()
        {
            var source = AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(CockpitAnalogInstrumentAuthoring.LayoutAssetPath);
            var frozen = AssetDatabase.LoadAllAssetsAtPath(CockpitInstrumentV2EditorContract.SourceGlbPath).OfType<Mesh>().Single(m => m.name == CockpitInstrumentV2EditorContract.SourceMeshName);
            var components = BuildComponents(frozen);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            var meter = prefab.transform.Find(CockpitAnalogInstrumentAuthoring.MeterRendererPath);
            var rig = prefab.GetComponent<QuestFlightLab.Runtime.AircraftReferenceFrameRig>();
            var right = source.Bindings[0].dialRightLocal.normalized;
            var up = source.Bindings[0].dialUpLocal.normalized;
            var normal = source.Bindings[0].rotationAxisLocal.normalized;
            foreach (var c in components.Values)
            {
                var f = Frame(frozen, c, right, up, normal);
                if (f.Radius < .001f) continue;
                var pos = rig.AircraftVisualRoot.InverseTransformPoint(meter.TransformPoint(f.Center));
                Debug.Log($"TRAINER seed={c.Seed} count={c.Vertices.Count} center={f.Center:F7} visual={pos:F5} radius={f.Radius:F7}");
            }
            Debug.Log("TRAINER axes " + right.ToString("F6") + " " + up.ToString("F6") + " " + normal.ToString("F6"));
            foreach(var r in prefab.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("Meter") || r.name.Contains("White") || r.name.Contains("Instrument") || r.name.Contains("Knob")))
                Debug.Log("TRAINER renderer " + r.name + " materials=" + string.Join(",",r.sharedMaterials.Select(m => m == null ? "null" : m.name)));
        }
    }
}
