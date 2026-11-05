#include "qfl_jsbsim_native.h"

#include <FGFDMExec.h>
#include <initialization/FGInitialCondition.h>
#include <initialization/FGTrim.h>
#include <models/FGGroundReactions.h>
#include <models/FGLGear.h>
#include <models/FGMassBalance.h>
#include <models/FGPropulsion.h>
#include <models/FGPropagate.h>
#include <models/FGInertial.h>
#include <models/FGExternalReactions.h>
#include <models/FGAircraft.h>
#include <models/FGAccelerations.h>
#include <input_output/FGGroundCallback.h>
#include <models/FGAuxiliary.h>
#include <models/atmosphere/FGStandardAtmosphere.h>
#include <input_output/FGPropertyManager.h>
#include <simgear/misc/sg_path.hxx>

#include <algorithm>
#include <cmath>
#include <memory>
#include <string>
#include <array>
#include <cstddef>
#include <stdexcept>

#include "qfl_jsbsim_ground_v3.inc"

namespace {
constexpr int kApiVersion = 3;
constexpr double kFeetPerSecondToKnots = 1.0 / 1.6878098571011957;
thread_local std::string g_last_error;

#include "qfl_jsbsim_properties_v3.inc"

struct NativeInstance {
  std::unique_ptr<JSBSim::FGFDMExec> fdm;
  std::string data_root, aircraft_name, last_error;
  qfl_jsbsim_atmosphere atmosphere{};
  qfl_jsbsim_capabilities_v3 capabilities{};
  std::array<SGPropertyNode*, 8> stations{};
  std::array<SGPropertyNode*, 4> tanks{};
  std::array<std::array<SGPropertyNode*, 7>, 3> external{};
  SGPropertyNode* airbrake_command = nullptr;
  SGPropertyNode* airbrake_position = nullptr;
  PreparedGround* prepared_ground = nullptr; // Owned by this FGFDMExec's Inertial.
  std::array<SGPropertyNode*, static_cast<size_t>(HotProperty::Count)> hot{};
  std::array<SGPropertyNode*, 16> compression{};
  bool faulted = false;
  bool limited_loading = false;
  std::array<double,4> loading_limits{}; // min/max lb, min/max structural CG x in
  bool loaded = false;
  ~NativeInstance() {
    // JSBSim may read bound ground properties while untying them at teardown,
    // including a never-reset model at its default location. This is not a step.
    if(prepared_ground)prepared_ground->initializing_coordinates=true;
    fdm.reset();
  }
};

NativeInstance* instance(qfl_jsbsim_handle handle) { return reinterpret_cast<NativeInstance*>(handle); }
double clamp(double value, double minimum, double maximum) { return std::max(minimum, std::min(maximum, value)); }
void set_error(NativeInstance* native, const std::string& message) {
  g_last_error = message;
  if (native) native->last_error = message;
}
bool require_loaded(NativeInstance* native) {
  if (native && native->loaded && native->fdm) return true;
  set_error(native, "JSBSim instance is null or has no loaded aircraft.");
  return false;
}
double cached_get(const NativeInstance& n, HotProperty key) {
  auto node=n.hot[static_cast<size_t>(key)]; return node ? node->getDoubleValue() : 0.0;
}
void cached_set(NativeInstance& n, HotProperty key, double value) {
  auto node=n.hot[static_cast<size_t>(key)]; if(node) node->setDoubleValue(value);
}
double compression(const NativeInstance& n, int index) {
  return index<n.capabilities.contact_count && n.compression[index] ? n.compression[index]->getDoubleValue() : 0;
}
int contact_wow(const NativeInstance& n, int index) {
  return index<n.capabilities.contact_count && n.fdm->GetGroundReactions()->GetGearUnit(index)->GetWOW();
}
void set_property(JSBSim::FGFDMExec& fdm, const char* name, double value) { fdm.SetPropertyValue(name, value); }
double get_property(JSBSim::FGFDMExec& fdm, const char* name) { return fdm.GetPropertyValue(name); }
int catch_failure(NativeInstance* native, const char* operation) {
  try { throw; }
  catch (const std::exception& exception) { set_error(native, std::string(operation) + ": " + exception.what()); }
  catch (...) { set_error(native, std::string(operation) + ": unknown native exception"); }
  return 0;
}

void clear_commands(JSBSim::FGFDMExec& fdm, bool engine_running) {
  const char* zero_properties[] = {
    "ap/elevator_cmd", "ap/aileron_cmd", "ap/roll-cmd-norm-output",
    "fcs/aileron-cmd-norm", "fcs/elevator-cmd-norm", "fcs/rudder-cmd-norm",
    "fcs/steer-cmd-norm",
    "fcs/pitch-trim-cmd-norm", "fcs/roll-trim-cmd-norm", "fcs/yaw-trim-cmd-norm",
    "fcs/flap-cmd-norm", "fcs/left-brake-cmd-norm", "fcs/right-brake-cmd-norm",
    "fcs/center-brake-cmd-norm"
  };
  for (const char* property : zero_properties) set_property(fdm, property, 0.0);
  if (fdm.GetPropulsion()->GetNumEngines() == 0) return;
  set_property(fdm, "propulsion/starter_cmd", 0.0);
  const double idle = engine_running ? 0.10 : 0.0;
  set_property(fdm, "fcs/throttle-cmd-norm", idle);
  set_property(fdm, "fcs/throttle-cmd-norm[0]", idle);
  set_property(fdm, "fcs/mixture-cmd-norm", engine_running ? 1.0 : 0.0);
  set_property(fdm, "fcs/mixture-cmd-norm[0]", engine_running ? 1.0 : 0.0);
  set_property(fdm, "propulsion/active_engine", 0.0);
  set_property(fdm, "propulsion/magneto_cmd", engine_running ? 3.0 : 0.0);
  set_property(fdm, "propulsion/set-running", engine_running ? -1.0 : 0.0);
}

bool discover_capabilities(NativeInstance& n) {
  auto pm = n.fdm->GetPropertyManager();
  n.capabilities = {};
  n.stations.fill(nullptr); n.tanks.fill(nullptr);
  const char* limits[]={"qfl/loading/min-weight-lb","qfl/loading/max-weight-lb","qfl/loading/min-cg-x-in","qfl/loading/max-cg-x-in"};
  int limit_count=0;
  for(int i=0;i<4;++i) { auto node=pm->GetNode(limits[i],false); if(node){++limit_count;n.loading_limits[i]=node->getDoubleValue();} }
  n.limited_loading=limit_count==4;
  if((limit_count && limit_count!=4) || (n.aircraft_name.rfind("qfl_",0)==0 && !n.limited_loading)) {
    set_error(&n,"Reference aircraft requires a complete declared loading envelope.");return false;
  }
  if(n.limited_loading) {
    for(double v:n.loading_limits)if(!std::isfinite(v)){set_error(&n,"Nonfinite loading envelope.");return false;}
    if(n.loading_limits[0]<=0||n.loading_limits[1]<n.loading_limits[0]||n.loading_limits[3]<n.loading_limits[2]){set_error(&n,"Invalid loading envelope.");return false;}
  }
  for (auto& channel : n.external) channel.fill(nullptr);
  auto& c = n.capabilities;
  c.engine_count = static_cast<int>(n.fdm->GetPropulsion()->GetNumEngines());
  c.tank_count = static_cast<int>(n.fdm->GetPropulsion()->GetNumTanks());
  c.contact_count = n.fdm->GetGroundReactions()->GetNumGearUnits();
  if (c.tank_count > 4 || c.contact_count > 16) { set_error(&n, "Aircraft exceeds bounded tank/contact capacity."); return false; }
  for (int i = 0; i < 8; ++i) {
    n.stations[i] = pm->GetNode("inertia/pointmass-weight-lbs[" + std::to_string(i) + "]", false);
    if (n.stations[i]) ++c.station_count; else break;
  }
  if (pm->HasNode("inertia/pointmass-weight-lbs[8]")) { set_error(&n, "Aircraft exceeds bounded station capacity."); return false; }
  for (int i = 0; i < c.tank_count; ++i)
    n.tanks[i] = pm->GetNode("propulsion/tank[" + std::to_string(i) + "]/contents-lbs", false);
  for(size_t i=0;i<n.hot.size();++i) n.hot[i]=pm->GetNode(kHotPropertyNames[i],false);
  n.compression.fill(nullptr);
  for(int i=0;i<c.contact_count;++i) n.compression[i]=pm->GetNode("gear/unit["+std::to_string(i)+"]/compression-ft",false);
  // Commands may be bound by the generic FCS. Actual output establishes support.
  n.airbrake_position = pm->GetNode("fcs/speedbrake-pos-norm", false);
  n.airbrake_command = pm->GetNode("fcs/speedbrake-cmd-norm", false);
  auto declared_airbrake = pm->GetNode("qfl/capabilities/airbrake", false);
  c.has_airbrake = declared_airbrake && declared_airbrake->getDoubleValue() == 1 && n.airbrake_position && n.airbrake_command;
  const char* names[] = {"qfl_tow", "qfl_airflow", "qfl_airflow_moment"};
  const char* force[] = {"x", "y", "z", "magnitude", "location-x-in", "location-y-in", "location-z-in"};
  const char* moment[] = {"l", "m", "n", "magnitude-lbsft"};
  int* flags[] = {&c.has_tow_force, &c.has_airflow_force, &c.has_airflow_moment};
  for (int channel = 0; channel < 3; ++channel) {
    int found = 0, count = channel == 2 ? 4 : 7;
    for (int i = 0; i < count; ++i) {
      n.external[channel][i] = pm->GetNode(std::string("external_reactions/") + names[channel] + "/" + (channel == 2 ? moment[i] : force[i]), false);
      if (n.external[channel][i]) ++found;
    }
    if (found && found != count) { set_error(&n, "Incomplete declared external force channel."); return false; }
    *flags[channel] = found == count;
  }
  return true;
}

bool valid_mass(NativeInstance& n) {
  auto mass=n.fdm->GetMassBalance();const auto& j=mass->GetJ();
  for(int r=1;r<=3;++r)for(int c=1;c<=3;++c)if(!std::isfinite(j(r,c)))return false;
  const double det=j(1,1)*(j(2,2)*j(3,3)-j(2,3)*j(3,2))-j(1,2)*(j(2,1)*j(3,3)-j(2,3)*j(3,1))+j(1,3)*(j(2,1)*j(3,2)-j(2,2)*j(3,1));
  if(j(1,1)<=0||j(1,1)*j(2,2)-j(1,2)*j(2,1)<=0||det<=0)return false;
  double weight=mass->GetWeight(),cg=mass->GetXYZcg(1);
  if(!std::isfinite(weight)||weight<=0||!std::isfinite(cg))return false;
  for(int i=1;i<=3;++i)if(!std::isfinite(mass->GetXYZcg(i)))return false;
  return !n.limited_loading||(weight>=n.loading_limits[0]&&weight<=n.loading_limits[1]&&cg>=n.loading_limits[2]&&cg<=n.loading_limits[3]);
}
void recompute_loading(NativeInstance& n) {
  auto mass=n.fdm->GetMassBalance();auto prop=n.fdm->GetPropulsion();
  mass->in.TanksWeight=prop->GetTanksWeight();mass->in.TanksMoment=prop->GetTanksMoment();mass->in.TankInertia=prop->CalculateTankInertias();
  mass->Run(false);
}
}

#include "qfl_jsbsim_aircraft_v3.inc"

extern "C" {
int qfl_jsbsim_api_version(void) { return kApiVersion; }

qfl_jsbsim_handle qfl_jsbsim_create(void) {
  try { auto native = std::make_unique<NativeInstance>(); g_last_error.clear(); return native.release(); }
  catch (...) { catch_failure(nullptr, "create"); return nullptr; }
}

void qfl_jsbsim_destroy(qfl_jsbsim_handle handle) { delete instance(handle); }

int qfl_jsbsim_load_aircraft(qfl_jsbsim_handle handle, const char* data_root_utf8, const char* aircraft_name_utf8) {
  NativeInstance* native = instance(handle);
  if (!native || !data_root_utf8 || !aircraft_name_utf8) { set_error(native, "load_aircraft requires valid arguments."); return 0; }
  try {
    native->loaded = false;
    native->faulted = false;
    if(native->prepared_ground)native->prepared_ground->initializing_coordinates=true;
    native->prepared_ground = nullptr;
    native->fdm = std::make_unique<JSBSim::FGFDMExec>();
    native->fdm->SetDebugLevel(0);
    native->data_root = data_root_utf8;
    native->aircraft_name = aircraft_name_utf8;
    native->fdm->SetRootDir(SGPath(native->data_root));
    native->fdm->SetAircraftPath(SGPath("aircraft"));
    native->fdm->SetEnginePath(SGPath("engine"));
    native->fdm->SetSystemsPath(SGPath("systems"));
    if (!native->fdm->LoadModel(native->aircraft_name)) {
      set_error(native, "JSBSim could not load aircraft '" + native->aircraft_name + "'.");
      native->fdm.reset(); return 0;
    }
    native->fdm->DisableOutput();
    native->fdm->DisableInput();
    if (!discover_capabilities(*native)) { native->fdm.reset(); return 0; }
    native->loaded = true;
    native->last_error.clear();
    return 1;
  } catch (...) { native->fdm.reset(); native->loaded = false; return catch_failure(native, "load_aircraft"); }
}

int qfl_jsbsim_set_mass_configuration(qfl_jsbsim_handle handle, const qfl_jsbsim_mass_configuration* c) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !c) { if (native && !c) set_error(native, "mass configuration is required."); return 0; }
  if (native->aircraft_name != "c172x") { set_error(native, "Legacy mass mapping is C172-only; use typed station loading."); return 0; }
  try {
    const double point_masses[] = { c->pilot_pounds, c->copilot_pounds, c->rear_left_pounds, c->rear_right_pounds, c->baggage_pounds };
    for (int i = 0; i < 5; ++i) {
      const std::string property = "inertia/pointmass-weight-lbs[" + std::to_string(i) + "]";
      set_property(*native->fdm, property.c_str(), std::max(0.0, point_masses[i]));
    }
    set_property(*native->fdm, "propulsion/tank[0]/contents-lbs", std::max(0.0, c->left_fuel_pounds));
    set_property(*native->fdm, "propulsion/tank[1]/contents-lbs", std::max(0.0, c->right_fuel_pounds));
    return 1;
  } catch (...) { return catch_failure(native, "set_mass_configuration"); }
}

int qfl_jsbsim_reset(qfl_jsbsim_handle handle, const qfl_jsbsim_initial_conditions* ic) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !ic) { if (native && !ic) set_error(native, "reset requires initial conditions."); return 0; }
  if (ic->engine_running && native->capabilities.engine_count == 0) { set_error(native, "Engine-free aircraft cannot reset with a running engine."); return 0; }
  const double initial_values[] = {ic->latitude_degrees, ic->longitude_degrees, ic->altitude_msl_feet, ic->terrain_elevation_msl_feet, ic->calibrated_airspeed_knots, ic->heading_degrees, ic->pitch_degrees, ic->bank_degrees, ic->flight_path_angle_degrees};
  for (double v : initial_values) if (!std::isfinite(v)) { set_error(native, "Initial conditions must be finite."); return 0; }
  if(native->prepared_ground && !native->prepared_ground->Covers(ic->latitude_degrees,ic->longitude_degrees)) {
    set_error(native,"Reset outside prepared terrain coverage.");return 0;
  }
  try {
    JSBSim::FGFDMExec& fdm = *native->fdm;
    fdm.Setsim_time(0.0);
    clear_commands(fdm, ic->engine_running != 0);
    qfl_jsbsim_clear_external_loads_v3(handle);
    if (native->capabilities.has_airbrake) native->airbrake_command->setDoubleValue(0.0);
    struct InitialCoordinateGuard {
      PreparedGround* ground;
      explicit InitialCoordinateGuard(PreparedGround* g):ground(g){if(g)g->initializing_coordinates=true;}
      ~InitialCoordinateGuard(){if(ground)ground->initializing_coordinates=false;}
    } coordinate_guard(native->prepared_ground);
    set_property(fdm, "ic/lat-geod-deg", ic->latitude_degrees);
    set_property(fdm, "ic/long-gc-deg", ic->longitude_degrees);
    set_property(fdm, "ic/h-sl-ft", ic->altitude_msl_feet);
    set_property(fdm, "ic/terrain-elevation-ft", ic->terrain_elevation_msl_feet);
    set_property(fdm, "ic/psi-true-deg", ic->heading_degrees);
    set_property(fdm, "ic/theta-deg", ic->pitch_degrees);
    set_property(fdm, "ic/phi-deg", ic->bank_degrees);
    auto initial = fdm.GetIC();
    const auto& wind = native->atmosphere;
    initial->SetWindNEDFpsIC(wind.wind_north_feet_per_second,
                           wind.wind_east_feet_per_second,
                           wind.wind_down_feet_per_second);
    initial->SetVcalibratedKtsIC(std::max(0.0, ic->calibrated_airspeed_knots));
    // The pinned IC setters preserve their historical wind-relative vector
    // convention, while Auxiliary subtracts physical NED wind at runtime.
    // Resolve CAS at this altitude with JSBSim, then explicitly construct
    // ground = air + wind so RunIC returns a coherent physical state.
    constexpr double degrees_to_radians = 3.14159265358979323846 / 180.0;
    const double heading = ic->heading_degrees * degrees_to_radians;
    const double gamma = ic->flight_path_angle_degrees * degrees_to_radians;
    const double speed = initial->GetVtrueFpsIC();
    const bool stationary_start = ic->calibrated_airspeed_knots <= 0.0;
    initial->SetVNorthFpsIC(stationary_start ? 0.0 : speed * std::cos(gamma) * std::cos(heading) + wind.wind_north_feet_per_second);
    initial->SetVEastFpsIC(stationary_start ? 0.0 : speed * std::cos(gamma) * std::sin(heading) + wind.wind_east_feet_per_second);
    initial->SetVDownFpsIC(stationary_start ? 0.0 : -speed * std::sin(gamma) + wind.wind_down_feet_per_second);
    if(native->prepared_ground)native->prepared_ground->initializing_coordinates=false;
    if (!fdm.RunIC()) { set_error(native, "JSBSim RunIC failed."); return 0; }
    if(!valid_mass(*native)){native->faulted=true;set_error(native,"Invalid aircraft mass, CG or inertia after reset.");return 0;}
    // The original C172 startup is retained for regression equivalence. New
    // aircraft start only after RunIC has initialized ambient pressure and
    // propulsion inputs. Starting a piston before this can extinguish it.
    if (native->aircraft_name != "c172x" && ic->engine_running)
      fdm.GetPropulsion()->InitRunning(-1);
    native->last_error.clear();
    return 1;
  } catch (...) { return catch_failure(native, "reset"); }
}

int qfl_jsbsim_set_controls(qfl_jsbsim_handle handle, const qfl_jsbsim_controls* c) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !c) { if (native && !c) set_error(native, "controls are required."); return 0; }
  const double control_values[] = {c->aileron, c->elevator, c->rudder, c->throttle, c->mixture, c->carb_heat, c->elevator_trim, c->flaps, c->left_brake, c->right_brake};
  for (double v : control_values) if (!std::isfinite(v)) { set_error(native, "Controls must be finite."); return 0; }
  if (native->capabilities.engine_count == 0 && (c->throttle != 0 || c->mixture != 0 || c->carb_heat != 0 || c->flaps != 0)) { set_error(native, "Engine-free controls require zero powered/flap channels; airbrake has a dedicated channel."); return 0; }
  try {
    JSBSim::FGFDMExec& fdm = *native->fdm;
    const double throttle = clamp(c->throttle, 0.0, 1.0), mixture = clamp(c->mixture, 0.0, 1.0);
    cached_set(*native, HotProperty::FcsAileronCmdNorm, clamp(c->aileron, -1.0, 1.0));
    cached_set(*native, HotProperty::FcsElevatorCmdNorm, clamp(c->elevator, -1.0, 1.0));
    const double rudder = clamp(c->rudder, -1.0, 1.0);
    cached_set(*native, HotProperty::FcsRudderCmdNorm, rudder);
    // The C172 system definition maps rudder command to nose-wheel steering
    // during JSBSim's own flight-control update.  Do not duplicate or fight
    // that model-owned scheduling from the host bridge.
    if (native->capabilities.engine_count > 0) {
      cached_set(*native, HotProperty::FcsThrottleCmdNorm, throttle); cached_set(*native, HotProperty::FcsThrottleCmdNorm0, throttle);
      cached_set(*native, HotProperty::FcsMixtureCmdNorm, mixture); cached_set(*native, HotProperty::FcsMixtureCmdNorm0, mixture);
      cached_set(*native, HotProperty::SystemsCarbHeatNorm, clamp(c->carb_heat, 0.0, 1.0));
    }
    cached_set(*native, HotProperty::FcsPitchTrimCmdNorm, clamp(c->elevator_trim, -1.0, 1.0));
    if (native->capabilities.engine_count > 0) cached_set(*native, HotProperty::FcsFlapCmdNorm, clamp(c->flaps, 0.0, 1.0));
    cached_set(*native, HotProperty::FcsLeftBrakeCmdNorm, clamp(c->left_brake, 0.0, 1.0));
    cached_set(*native, HotProperty::FcsRightBrakeCmdNorm, clamp(c->right_brake, 0.0, 1.0));
    cached_set(*native, HotProperty::FcsCenterBrakeCmdNorm, clamp(std::max(c->left_brake, c->right_brake), 0.0, 1.0));
    return 1;
  } catch (...) { return catch_failure(native, "set_controls"); }
}

int qfl_jsbsim_set_atmosphere(qfl_jsbsim_handle handle, const qfl_jsbsim_atmosphere* a) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !a) { if (native && !a) set_error(native, "atmosphere is required."); return 0; }
  if (!std::isfinite(a->wind_north_feet_per_second) ||
      !std::isfinite(a->wind_east_feet_per_second) ||
      !std::isfinite(a->wind_down_feet_per_second)) {
    set_error(native, "atmosphere wind must be finite."); return 0;
  }
  try {
    cached_set(*native, HotProperty::AtmosphereWindNorthFps, a->wind_north_feet_per_second);
    cached_set(*native, HotProperty::AtmosphereWindEastFps, a->wind_east_feet_per_second);
    cached_set(*native, HotProperty::AtmosphereWindDownFps, a->wind_down_feet_per_second);
    native->atmosphere = *a;
    return 1;
  } catch (...) { return catch_failure(native, "set_atmosphere"); }
}

int qfl_jsbsim_set_terrain_elevation(qfl_jsbsim_handle handle, double elevation) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !std::isfinite(elevation)) { set_error(native, "terrain elevation must be finite."); return 0; }
  try { cached_set(*native, HotProperty::PositionTerrainElevationAslFt, elevation); return 1; }
  catch (...) { return catch_failure(native, "set_terrain_elevation"); }
}

int qfl_jsbsim_trim(qfl_jsbsim_handle handle, const qfl_jsbsim_trim_request* request, qfl_jsbsim_trim_result* result) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !request || !result) { set_error(native, "trim requires request and result structs."); return 0; }
  if (native->capabilities.engine_count == 0) { set_error(native, "Powered trim is unavailable for engine-free aircraft; use a constrained descending-glide solve."); return 0; }
  try {
    JSBSim::FGTrim trim(native->fdm.get(), request->mode == 1 ? JSBSim::tFull : JSBSim::tLongitudinal);
    if (request->max_cycles > 0) trim.SetMaxCycles(request->max_cycles);
    if (request->max_cycles_per_axis > 0) trim.SetMaxCyclesPerAxis(request->max_cycles_per_axis);
    if (request->tolerance > 0.0) trim.SetTolerance(request->tolerance);
    trim.SetDebug(0);
    result->success = trim.DoTrim() ? 1 : 0;
    JSBSim::FGFDMExec& fdm = *native->fdm;
    result->aileron_command = cached_get(*native, HotProperty::FcsAileronCmdNorm);
    result->elevator_command = cached_get(*native, HotProperty::FcsElevatorCmdNorm);
    result->rudder_command = cached_get(*native, HotProperty::FcsRudderCmdNorm);
    result->throttle_command = cached_get(*native, HotProperty::FcsThrottleCmdNorm);
    result->pitch_trim_command = cached_get(*native, HotProperty::FcsPitchTrimCmdNorm);
    result->angle_of_attack_degrees = cached_get(*native, HotProperty::AeroAlphaDeg);
    result->pitch_degrees = cached_get(*native, HotProperty::AttitudeThetaDeg);
    result->bank_degrees = cached_get(*native, HotProperty::AttitudePhiDeg);
    if (!result->success) { set_error(native, "JSBSim could not trim the requested state."); return 0; }
    native->last_error.clear(); return 1;
  } catch (...) { return catch_failure(native, "trim"); }
}

int qfl_jsbsim_advance(qfl_jsbsim_handle handle, double dt) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native)) return 0;
  if (!std::isfinite(dt) || dt <= 0.0) { set_error(native, "advance requires a finite positive timestep."); return 0; }
  if(native->faulted){set_error(native,"Native session is faulted; reload aircraft before stepping.");return 0;}
  try {
    native->fdm->Setdt(dt);
    if(native->capabilities.has_tow_force) {
      // FGFDMExec propagates using the acceleration from its preceding Run,
      // then evaluates forces at the new state. Refresh only the newly
      // submitted external loads at the current state before propagation.
      // No second FCS/engine/weather tick or altered integrator history.
      // All other Aircraft/Accelerations inputs already describe this same
      // state from RunIC/the preceding Run. Legacy C172 never takes this path.
      auto external=native->fdm->GetExternalReactions();
      auto aircraft=native->fdm->GetAircraft();
      auto acceleration=native->fdm->GetAccelerations();
      external->Run(false);
      aircraft->in.ExternalForce=external->GetForces();
      aircraft->in.ExternalMoment=external->GetMoments();
      aircraft->Run(false);
      acceleration->in.Force=aircraft->GetForces();
      acceleration->in.Moment=aircraft->GetMoments();
      acceleration->in.DeltaT=dt;
      acceleration->Run(false);
    }
    if (!native->fdm->Run()) { native->faulted=true;set_error(native, "JSBSim Run returned false."); return 0; }
    auto p=native->fdm->GetPropagate();bool valid=valid_mass(*native);
    for(int i=1;i<=3;++i) valid=valid&&std::isfinite(p->GetLocation(i))&&std::isfinite(p->GetUVW()(i))&&std::isfinite(p->GetPQR()(i));
    if(!valid){native->faulted=true;qfl_jsbsim_clear_external_loads_v3(handle);set_error(native,"Native aircraft state, mass, CG or inertia became invalid; halted.");return 0;}
    return 1;
  }
  catch (...) { native->faulted=true;qfl_jsbsim_clear_external_loads_v3(handle);return catch_failure(native, "advance"); }
}

int qfl_jsbsim_get_state(qfl_jsbsim_handle handle, qfl_jsbsim_state* s) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !s) { set_error(native, "state output is required."); return 0; }
  try {
    JSBSim::FGFDMExec& fdm = *native->fdm;
    s->simulation_time_seconds = fdm.GetSimTime();
    s->latitude_degrees = cached_get(*native, HotProperty::PositionLatGeodDeg); s->longitude_degrees = cached_get(*native, HotProperty::PositionLongGcDeg);
    s->altitude_msl_feet = cached_get(*native, HotProperty::PositionHSlFt); s->altitude_agl_feet = cached_get(*native, HotProperty::PositionHAglFt);
    s->calibrated_airspeed_knots = cached_get(*native, HotProperty::VelocitiesVcKts);
    s->ground_speed_knots = cached_get(*native, HotProperty::VelocitiesVgFps) * kFeetPerSecondToKnots;
    s->vertical_speed_feet_per_minute = cached_get(*native, HotProperty::VelocitiesHDotFps) * 60.0;
    s->heading_degrees = cached_get(*native, HotProperty::AttitudePsiDeg); s->pitch_degrees = cached_get(*native, HotProperty::AttitudeThetaDeg); s->bank_degrees = cached_get(*native, HotProperty::AttitudePhiDeg);
    s->angle_of_attack_degrees = cached_get(*native, HotProperty::AeroAlphaDeg); s->sideslip_degrees = cached_get(*native, HotProperty::AeroBetaDeg);
    s->velocity_north_feet_per_second = cached_get(*native, HotProperty::VelocitiesVNorthFps); s->velocity_east_feet_per_second = cached_get(*native, HotProperty::VelocitiesVEastFps); s->velocity_down_feet_per_second = cached_get(*native, HotProperty::VelocitiesVDownFps);
    s->roll_rate_radians_per_second = cached_get(*native, HotProperty::VelocitiesPRadSec); s->pitch_rate_radians_per_second = cached_get(*native, HotProperty::VelocitiesQRadSec); s->yaw_rate_radians_per_second = cached_get(*native, HotProperty::VelocitiesRRadSec);
    s->engine_rpm = native->capabilities.engine_count ? cached_get(*native, HotProperty::PropulsionEngineEngineRpm) : 0.0; s->load_factor_g = cached_get(*native, HotProperty::ForcesLoadFactor);
    s->weight_on_wheels = cached_get(*native, HotProperty::GearWow) > 0.5 ? 1 : 0;
    return 1;
  } catch (...) { return catch_failure(native, "get_state"); }
}

int qfl_jsbsim_get_diagnostics(qfl_jsbsim_handle handle, qfl_jsbsim_diagnostics* d) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !d) { set_error(native, "diagnostics output is required."); return 0; }
  try {
    JSBSim::FGFDMExec& fdm = *native->fdm;
#define GP(field, property) d->field = cached_get(*native, HotProperty::property)
    GP(true_airspeed_knots, VelocitiesVtrueKts); GP(equivalent_airspeed_knots, VelocitiesVeKts); GP(density_slugs_per_cubic_foot, AtmosphereRhoSlugsFt3);
    GP(terrain_elevation_msl_feet, PositionTerrainElevationAslFt); GP(weight_pounds, InertiaWeightLbs); GP(cg_x_inches, InertiaCgXIn); GP(cg_y_inches, InertiaCgYIn); GP(cg_z_inches, InertiaCgZIn);
    d->left_fuel_pounds = native->capabilities.tank_count > 0 ? native->tanks[0]->getDoubleValue() : 0;
    d->right_fuel_pounds = native->capabilities.tank_count > 1 ? native->tanks[1]->getDoubleValue() : 0;
    GP(left_aileron_position, FcsLeftAileronPosNorm); GP(right_aileron_position, FcsRightAileronPosNorm); GP(elevator_position, FcsElevatorPosNorm); GP(rudder_position, FcsRudderPosNorm); GP(flap_position, FcsFlapPosNorm);
    GP(total_force_x_pounds, ForcesFbxTotalLbs); GP(total_force_y_pounds, ForcesFbyTotalLbs); GP(total_force_z_pounds, ForcesFbzTotalLbs);
    GP(aero_force_x_pounds, ForcesFbxAeroLbs); GP(aero_force_y_pounds, ForcesFbyAeroLbs); GP(aero_force_z_pounds, ForcesFbzAeroLbs);
    GP(propulsion_force_x_pounds, ForcesFbxPropLbs); GP(propulsion_force_y_pounds, ForcesFbyPropLbs); GP(propulsion_force_z_pounds, ForcesFbzPropLbs);
    GP(gear_force_x_pounds, ForcesFbxGearLbs); GP(gear_force_y_pounds, ForcesFbyGearLbs); GP(gear_force_z_pounds, ForcesFbzGearLbs);
    GP(total_roll_moment_pound_feet, MomentsLTotalLbsft); GP(total_pitch_moment_pound_feet, MomentsMTotalLbsft); GP(total_yaw_moment_pound_feet, MomentsNTotalLbsft);
    GP(aero_roll_moment_pound_feet, MomentsLAeroLbsft); GP(aero_pitch_moment_pound_feet, MomentsMAeroLbsft); GP(aero_yaw_moment_pound_feet, MomentsNAeroLbsft);
    GP(propulsion_roll_moment_pound_feet, MomentsLPropLbsft); GP(propulsion_pitch_moment_pound_feet, MomentsMPropLbsft); GP(propulsion_yaw_moment_pound_feet, MomentsNPropLbsft);
    GP(gear_roll_moment_pound_feet, MomentsLGearLbsft); GP(gear_pitch_moment_pound_feet, MomentsMGearLbsft); GP(gear_yaw_moment_pound_feet, MomentsNGearLbsft);
#undef GP
    d->gear0_compression_feet = compression(*native, 0); d->gear1_compression_feet = compression(*native, 1); d->gear2_compression_feet = compression(*native, 2);
    // Steerable (non-castering) gear publishes the authoritative angle under
    // fcs/steer-pos-deg[index].  gear/unit[index]/steering-angle-deg exists
    // only for castering gear and silently read as zero for this C172 nosewheel.
    auto ground = fdm.GetGroundReactions();
    d->gear0_steering_degrees = ground->GetNumGearUnits() > 0 ? ground->GetGearUnit(0)->GetSteerAngleDeg() : 0.0;
    d->gear1_steering_degrees = ground->GetNumGearUnits() > 1 ? ground->GetGearUnit(1)->GetSteerAngleDeg() : 0.0;
    d->gear2_steering_degrees = ground->GetNumGearUnits() > 2 ? ground->GetGearUnit(2)->GetSteerAngleDeg() : 0.0;
    d->steering_command = ground->GetDsCmd();
    d->gear0_weight_on_wheels = contact_wow(*native, 0) > 0.5; d->gear1_weight_on_wheels = contact_wow(*native, 1) > 0.5; d->gear2_weight_on_wheels = contact_wow(*native, 2) > 0.5;
    d->gear0_steer_type = ground->GetNumGearUnits() > 0 ? ground->GetGearUnit(0)->GetSteerType() : -1;
    d->gear0_steerable = ground->GetNumGearUnits() > 0 && ground->GetGearUnit(0)->GetSteerable() ? 1 : 0;
    return 1;
  } catch (...) { return catch_failure(native, "get_diagnostics"); }
}

const char* qfl_jsbsim_last_error(qfl_jsbsim_handle handle) { NativeInstance* native = instance(handle); return native ? native->last_error.c_str() : g_last_error.c_str(); }
}
