using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Flight.Soaring;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static class SoaringEnergyVerification
    {
        [Serializable] private sealed class Case {public int rate;public double damping,initialEnergy,maximumEnergy,finalEnergy,maximumGrowthFraction;public bool passed;public string error;}
        [Serializable] private sealed class Report
        {
            public string scope="Isolated native zero-aero/zero-thrust equal-mass bodies with CG hooks. Relative kinetic + rope elastic + second-order spherical gravitational/centrifugal tidal potential removes common free-fall work. J2 and higher tidal orders omitted (60m separation versus >6300km radius). No rotation induced by CG hooks; off-CG signs tested separately in native_foundation. No full-aircraft fidelity claim.";
            public double duration=60,tolerance=.01;public List<Case> cases=new List<Case>();public bool passed;
        }
        public static void Run()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");Directory.CreateDirectory(output);var report=new Report();
            string data=Path.Combine(output,"isolated_fixture");string model=Path.Combine(data,"aircraft/qfl_energy/qfl_energy.xml");Directory.CreateDirectory(Path.GetDirectoryName(model));
            var xml=XDocument.Load(Path.Combine(JSBSimRuntimeData.EditorPackageDataRoot,"aircraft/qfl_sgs233_ref/qfl_sgs233_ref.xml"));var root=xml.Root;root.SetAttributeValue("name","qfl_energy");
            foreach(var axis in root.Element("aerodynamics").Elements("axis"))axis.RemoveNodes();
            var mass=root.Element("mass_balance");mass.Elements("pointmass").Remove();mass.Element("emptywt").Value="950";
            var cg=mass.Elements("location").First(x=>(string)x.Attribute("name")=="CG");cg.Element("x").Value="81.56315789473685";cg.Element("z").Value="-2.6778947368421053";
            var hook=root.Element("external_reactions").Elements("force").First(x=>(string)x.Attribute("name")=="qfl_tow").Element("location");hook.ReplaceNodes(cg.Elements().Select(x=>new XElement(x)));xml.Save(model);
            void Need(bool ok,JSBSimNativeSession s){if(!ok)throw new InvalidOperationException(s.LastError);}
            foreach(int rate in new[]{120,240,480})foreach(double damping in new[]{0d,700d})
            {
                var c=new Case{rate=rate,damping=damping};report.cases.Add(c);var definition=ScriptableObject.CreateInstance<TowlineDefinition>();definition.dampingNewtonSecondsPerMeter=damping;
                try
                {
                    using(var a=new JSBSimNativeSession())using(var b=new JSBSimNativeSession())
                    {
                        var config=new JSBSimAircraftConfiguration{modelName="qfl_energy"};Need(a.Initialize(data,config),a);Need(b.Initialize(data,config),b);
                        var initial=new JSBSimNativeInitialConditions{latitudeDegrees=0,longitudeDegrees=0,altitudeMslFeet=100000,terrainElevationMslFeet=0,headingDegrees=90};Need(a.Reset(initial),a);
                        // Equatorial geocentric radius plus altitude: native reset is radial.
                        initial.longitudeDegrees=60.1/(6378137+30480)*180/Math.PI;Need(b.Reset(initial),b);
                        var rope=new TowlineSolver(definition);rope.HookUp();
                        double Energy(JSBSimNativeAttachmentState ga,JSBSimNativeAttachmentState gb,TowlineSnapshot line)
                        {
                            double vx=gb.velocityEcefXMps-ga.velocityEcefXMps,vy=gb.velocityEcefYMps-ga.velocityEcefYMps,vz=gb.velocityEcefZMps-ga.velocityEcefZMps;
                            double dx=gb.positionEcefXM-ga.positionEcefXM,dy=gb.positionEcefYM-ga.positionEcefYM,dz=gb.positionEcefZM-ga.positionEcefZM;
                            double x=(gb.positionEcefXM+ga.positionEcefXM)*.5,y=(gb.positionEcefYM+ga.positionEcefYM)*.5,z=(gb.positionEcefZM+ga.positionEcefZM)*.5;
                            double radius=Math.Sqrt(x*x+y*y+z*z),radial=(x*dx+y*dy+z*dz)/radius;
                            double tidal=3.986004418e14/(radius*radius*radius)*(dx*dx+dy*dy+dz*dz-3*radial*radial)-7.292115e-5*7.292115e-5*(dx*dx+dy*dy);
                            return .25*950*.45359237*(vx*vx+vy*vy+vz*vz+tidal)+line.elasticEnergyJoules;
                        }
                        for(int tick=0;tick<=60*rate;tick++)
                        {
                            Need(a.GetAttachment(out var ga),a);Need(b.GetAttachment(out var gb),b);if(!rope.Evaluate(ga,gb,out var line))throw new InvalidOperationException("Rope invalid");
                            double energy=Energy(ga,gb,line);if(tick==0)c.initialEnergy=energy;c.maximumEnergy=Math.Max(c.maximumEnergy,energy);c.finalEnergy=energy;
                            if(tick==60*rate)break;
                            Need(a.SetExternalLoad(ga.EcefForceToBody(line.forceEcefXNewtons,line.forceEcefYNewtons,line.forceEcefZNewtons)),a);
                            Need(b.SetExternalLoad(gb.EcefForceToBody(-line.forceEcefXNewtons,-line.forceEcefYNewtons,-line.forceEcefZNewtons)),b);
                            Need(a.SubmitPendingInputs(),a);Need(b.SubmitPendingInputs(),b);Need(a.AdvanceSubmitted(1d/rate),a);Need(b.AdvanceSubmitted(1d/rate),b);
                        }
                        c.maximumGrowthFraction=(c.maximumEnergy-c.initialEnergy)/c.initialEnergy;c.passed=c.maximumGrowthFraction<=.01 && (damping==0||c.finalEnergy<c.initialEnergy);
                    }
                }
                catch(Exception ex){c.error=ex.ToString();Debug.LogException(ex);}
                finally{UnityEngine.Object.DestroyImmediate(definition);}
            }
            report.passed=report.cases.All(c=>c.passed);File.WriteAllText(Path.Combine(output,"energy_verification.json"),JsonUtility.ToJson(report,true));EditorApplication.Exit(report.passed?0:1);
        }
    }
}
