#include "qfl_jsbsim_native.h"

#include <FGFDMExec.h>
#include <initialization/FGTrim.h>
#include <models/FGGroundReactions.h>
#include <models/FGLGear.h>
#include <simgear/misc/sg_path.hxx>

#include <algorithm>
#include <cmath>
#include <memory>
#include <string>

namespace {
constexpr int kApiVersion = 2;
constexpr double kFeetPerSecondToKnots = 1.0 / 1.6878098571011957;
thread_local std::string g_last_error;

struct NativeInstance {
  std::unique_ptr<JSBSim::FGFDMExec> fdm;
  std::string data_root, aircraft_name, last_error;
  bool loaded = false;
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
    "fcs/center-brake-cmd-norm", "propulsion/starter_cmd"
  };
  for (const char* property : zero_properties) set_property(fdm, property, 0.0);
  const double idle = engine_running ? 0.10 : 0.0;
  set_property(fdm, "fcs/throttle-cmd-norm", idle);
  set_property(fdm, "fcs/throttle-cmd-norm[0]", idle);
  set_property(fdm, "fcs/mixture-cmd-norm", engine_running ? 1.0 : 0.0);
  set_property(fdm, "fcs/mixture-cmd-norm[0]", engine_running ? 1.0 : 0.0);
  set_property(fdm, "propulsion/active_engine", 0.0);
  set_property(fdm, "propulsion/magneto_cmd", engine_running ? 3.0 : 0.0);
  set_property(fdm, "propulsion/set-running", engine_running ? -1.0 : 0.0);
}

double gear_property(JSBSim::FGFDMExec& fdm, int index, const char* suffix) {
  return get_property(fdm, ("gear/unit[" + std::to_string(index) + "]/" + suffix).c_str());
}
}

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
    native->loaded = true;
    native->last_error.clear();
    return 1;
  } catch (...) { native->fdm.reset(); native->loaded = false; return catch_failure(native, "load_aircraft"); }
}

int qfl_jsbsim_set_mass_configuration(qfl_jsbsim_handle handle, const qfl_jsbsim_mass_configuration* c) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !c) { if (native && !c) set_error(native, "mass configuration is required."); return 0; }
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
  try {
    JSBSim::FGFDMExec& fdm = *native->fdm;
    fdm.Setsim_time(0.0);
    clear_commands(fdm, ic->engine_running != 0);
    set_property(fdm, "ic/lat-geod-deg", ic->latitude_degrees);
    set_property(fdm, "ic/long-gc-deg", ic->longitude_degrees);
    set_property(fdm, "ic/h-sl-ft", ic->altitude_msl_feet);
    set_property(fdm, "ic/terrain-elevation-ft", ic->terrain_elevation_msl_feet);
    set_property(fdm, "ic/vc-kts", std::max(0.0, ic->calibrated_airspeed_knots));
    set_property(fdm, "ic/gamma-deg", ic->flight_path_angle_degrees);
    set_property(fdm, "ic/psi-true-deg", ic->heading_degrees);
    set_property(fdm, "ic/theta-deg", ic->pitch_degrees);
    set_property(fdm, "ic/phi-deg", ic->bank_degrees);
    if (!fdm.RunIC()) { set_error(native, "JSBSim RunIC failed."); return 0; }
    native->last_error.clear();
    return 1;
  } catch (...) { return catch_failure(native, "reset"); }
}

int qfl_jsbsim_set_controls(qfl_jsbsim_handle handle, const qfl_jsbsim_controls* c) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !c) { if (native && !c) set_error(native, "controls are required."); return 0; }
  try {
    JSBSim::FGFDMExec& fdm = *native->fdm;
    const double throttle = clamp(c->throttle, 0.0, 1.0), mixture = clamp(c->mixture, 0.0, 1.0);
    set_property(fdm, "fcs/aileron-cmd-norm", clamp(c->aileron, -1.0, 1.0));
    set_property(fdm, "fcs/elevator-cmd-norm", clamp(c->elevator, -1.0, 1.0));
    const double rudder = clamp(c->rudder, -1.0, 1.0);
    set_property(fdm, "fcs/rudder-cmd-norm", rudder);
    // The C172 system definition maps rudder command to nose-wheel steering
    // during JSBSim's own flight-control update.  Do not duplicate or fight
    // that model-owned scheduling from the host bridge.
    set_property(fdm, "fcs/throttle-cmd-norm", throttle); set_property(fdm, "fcs/throttle-cmd-norm[0]", throttle);
    set_property(fdm, "fcs/mixture-cmd-norm", mixture); set_property(fdm, "fcs/mixture-cmd-norm[0]", mixture);
    set_property(fdm, "fcs/pitch-trim-cmd-norm", clamp(c->elevator_trim, -1.0, 1.0));
    set_property(fdm, "fcs/flap-cmd-norm", clamp(c->flaps, 0.0, 1.0));
    set_property(fdm, "fcs/left-brake-cmd-norm", clamp(c->left_brake, 0.0, 1.0));
    set_property(fdm, "fcs/right-brake-cmd-norm", clamp(c->right_brake, 0.0, 1.0));
    set_property(fdm, "fcs/center-brake-cmd-norm", clamp(std::max(c->left_brake, c->right_brake), 0.0, 1.0));
    set_property(fdm, "systems/carb-heat-norm", clamp(c->carb_heat, 0.0, 1.0));
    return 1;
  } catch (...) { return catch_failure(native, "set_controls"); }
}

int qfl_jsbsim_set_atmosphere(qfl_jsbsim_handle handle, const qfl_jsbsim_atmosphere* a) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !a) { if (native && !a) set_error(native, "atmosphere is required."); return 0; }
  try {
    set_property(*native->fdm, "atmosphere/wind-north-fps", a->wind_north_feet_per_second);
    set_property(*native->fdm, "atmosphere/wind-east-fps", a->wind_east_feet_per_second);
    set_property(*native->fdm, "atmosphere/wind-down-fps", a->wind_down_feet_per_second);
    return 1;
  } catch (...) { return catch_failure(native, "set_atmosphere"); }
}

int qfl_jsbsim_set_terrain_elevation(qfl_jsbsim_handle handle, double elevation) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !std::isfinite(elevation)) { set_error(native, "terrain elevation must be finite."); return 0; }
  try { set_property(*native->fdm, "position/terrain-elevation-asl-ft", elevation); return 1; }
  catch (...) { return catch_failure(native, "set_terrain_elevation"); }
}

int qfl_jsbsim_trim(qfl_jsbsim_handle handle, const qfl_jsbsim_trim_request* request, qfl_jsbsim_trim_result* result) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !request || !result) { set_error(native, "trim requires request and result structs."); return 0; }
  try {
    JSBSim::FGTrim trim(native->fdm.get(), request->mode == 1 ? JSBSim::tFull : JSBSim::tLongitudinal);
    if (request->max_cycles > 0) trim.SetMaxCycles(request->max_cycles);
    if (request->max_cycles_per_axis > 0) trim.SetMaxCyclesPerAxis(request->max_cycles_per_axis);
    if (request->tolerance > 0.0) trim.SetTolerance(request->tolerance);
    trim.SetDebug(0);
    result->success = trim.DoTrim() ? 1 : 0;
    JSBSim::FGFDMExec& fdm = *native->fdm;
    result->aileron_command = get_property(fdm, "fcs/aileron-cmd-norm");
    result->elevator_command = get_property(fdm, "fcs/elevator-cmd-norm");
    result->rudder_command = get_property(fdm, "fcs/rudder-cmd-norm");
    result->throttle_command = get_property(fdm, "fcs/throttle-cmd-norm");
    result->pitch_trim_command = get_property(fdm, "fcs/pitch-trim-cmd-norm");
    result->angle_of_attack_degrees = get_property(fdm, "aero/alpha-deg");
    result->pitch_degrees = get_property(fdm, "attitude/theta-deg");
    result->bank_degrees = get_property(fdm, "attitude/phi-deg");
    if (!result->success) { set_error(native, "JSBSim could not trim the requested state."); return 0; }
    native->last_error.clear(); return 1;
  } catch (...) { return catch_failure(native, "trim"); }
}

int qfl_jsbsim_advance(qfl_jsbsim_handle handle, double dt) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native)) return 0;
  if (!std::isfinite(dt) || dt <= 0.0) { set_error(native, "advance requires a finite positive timestep."); return 0; }
  try { native->fdm->Setdt(dt); if (!native->fdm->Run()) { set_error(native, "JSBSim Run returned false."); return 0; } return 1; }
  catch (...) { return catch_failure(native, "advance"); }
}

int qfl_jsbsim_get_state(qfl_jsbsim_handle handle, qfl_jsbsim_state* s) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !s) { set_error(native, "state output is required."); return 0; }
  try {
    JSBSim::FGFDMExec& fdm = *native->fdm;
    s->simulation_time_seconds = fdm.GetSimTime();
    s->latitude_degrees = get_property(fdm, "position/lat-geod-deg"); s->longitude_degrees = get_property(fdm, "position/long-gc-deg");
    s->altitude_msl_feet = get_property(fdm, "position/h-sl-ft"); s->altitude_agl_feet = get_property(fdm, "position/h-agl-ft");
    s->calibrated_airspeed_knots = get_property(fdm, "velocities/vc-kts");
    s->ground_speed_knots = get_property(fdm, "velocities/vg-fps") * kFeetPerSecondToKnots;
    s->vertical_speed_feet_per_minute = get_property(fdm, "velocities/h-dot-fps") * 60.0;
    s->heading_degrees = get_property(fdm, "attitude/psi-deg"); s->pitch_degrees = get_property(fdm, "attitude/theta-deg"); s->bank_degrees = get_property(fdm, "attitude/phi-deg");
    s->angle_of_attack_degrees = get_property(fdm, "aero/alpha-deg"); s->sideslip_degrees = get_property(fdm, "aero/beta-deg");
    s->velocity_north_feet_per_second = get_property(fdm, "velocities/v-north-fps"); s->velocity_east_feet_per_second = get_property(fdm, "velocities/v-east-fps"); s->velocity_down_feet_per_second = get_property(fdm, "velocities/v-down-fps");
    s->roll_rate_radians_per_second = get_property(fdm, "velocities/p-rad_sec"); s->pitch_rate_radians_per_second = get_property(fdm, "velocities/q-rad_sec"); s->yaw_rate_radians_per_second = get_property(fdm, "velocities/r-rad_sec");
    s->engine_rpm = get_property(fdm, "propulsion/engine/engine-rpm"); s->load_factor_g = get_property(fdm, "forces/load-factor");
    s->weight_on_wheels = get_property(fdm, "gear/wow") > 0.5 ? 1 : 0;
    return 1;
  } catch (...) { return catch_failure(native, "get_state"); }
}

int qfl_jsbsim_get_diagnostics(qfl_jsbsim_handle handle, qfl_jsbsim_diagnostics* d) {
  NativeInstance* native = instance(handle);
  if (!require_loaded(native) || !d) { set_error(native, "diagnostics output is required."); return 0; }
  try {
    JSBSim::FGFDMExec& fdm = *native->fdm;
#define GP(field, property) d->field = get_property(fdm, property)
    GP(true_airspeed_knots, "velocities/vtrue-kts"); GP(equivalent_airspeed_knots, "velocities/ve-kts"); GP(density_slugs_per_cubic_foot, "atmosphere/rho-slugs_ft3");
    GP(terrain_elevation_msl_feet, "position/terrain-elevation-asl-ft"); GP(weight_pounds, "inertia/weight-lbs"); GP(cg_x_inches, "inertia/cg-x-in"); GP(cg_y_inches, "inertia/cg-y-in"); GP(cg_z_inches, "inertia/cg-z-in");
    GP(left_fuel_pounds, "propulsion/tank[0]/contents-lbs"); GP(right_fuel_pounds, "propulsion/tank[1]/contents-lbs");
    GP(left_aileron_position, "fcs/left-aileron-pos-norm"); GP(right_aileron_position, "fcs/right-aileron-pos-norm"); GP(elevator_position, "fcs/elevator-pos-norm"); GP(rudder_position, "fcs/rudder-pos-norm"); GP(flap_position, "fcs/flap-pos-norm");
    GP(total_force_x_pounds, "forces/fbx-total-lbs"); GP(total_force_y_pounds, "forces/fby-total-lbs"); GP(total_force_z_pounds, "forces/fbz-total-lbs");
    GP(aero_force_x_pounds, "forces/fbx-aero-lbs"); GP(aero_force_y_pounds, "forces/fby-aero-lbs"); GP(aero_force_z_pounds, "forces/fbz-aero-lbs");
    GP(propulsion_force_x_pounds, "forces/fbx-prop-lbs"); GP(propulsion_force_y_pounds, "forces/fby-prop-lbs"); GP(propulsion_force_z_pounds, "forces/fbz-prop-lbs");
    GP(gear_force_x_pounds, "forces/fbx-gear-lbs"); GP(gear_force_y_pounds, "forces/fby-gear-lbs"); GP(gear_force_z_pounds, "forces/fbz-gear-lbs");
    GP(total_roll_moment_pound_feet, "moments/l-total-lbsft"); GP(total_pitch_moment_pound_feet, "moments/m-total-lbsft"); GP(total_yaw_moment_pound_feet, "moments/n-total-lbsft");
    GP(aero_roll_moment_pound_feet, "moments/l-aero-lbsft"); GP(aero_pitch_moment_pound_feet, "moments/m-aero-lbsft"); GP(aero_yaw_moment_pound_feet, "moments/n-aero-lbsft");
    GP(propulsion_roll_moment_pound_feet, "moments/l-prop-lbsft"); GP(propulsion_pitch_moment_pound_feet, "moments/m-prop-lbsft"); GP(propulsion_yaw_moment_pound_feet, "moments/n-prop-lbsft");
    GP(gear_roll_moment_pound_feet, "moments/l-gear-lbsft"); GP(gear_pitch_moment_pound_feet, "moments/m-gear-lbsft"); GP(gear_yaw_moment_pound_feet, "moments/n-gear-lbsft");
#undef GP
    d->gear0_compression_feet = gear_property(fdm, 0, "compression-ft"); d->gear1_compression_feet = gear_property(fdm, 1, "compression-ft"); d->gear2_compression_feet = gear_property(fdm, 2, "compression-ft");
    // Steerable (non-castering) gear publishes the authoritative angle under
    // fcs/steer-pos-deg[index].  gear/unit[index]/steering-angle-deg exists
    // only for castering gear and silently read as zero for this C172 nosewheel.
    auto ground = fdm.GetGroundReactions();
    d->gear0_steering_degrees = ground->GetNumGearUnits() > 0 ? ground->GetGearUnit(0)->GetSteerAngleDeg() : 0.0;
    d->gear1_steering_degrees = ground->GetNumGearUnits() > 1 ? ground->GetGearUnit(1)->GetSteerAngleDeg() : 0.0;
    d->gear2_steering_degrees = ground->GetNumGearUnits() > 2 ? ground->GetGearUnit(2)->GetSteerAngleDeg() : 0.0;
    d->steering_command = ground->GetDsCmd();
    d->gear0_weight_on_wheels = gear_property(fdm, 0, "WOW") > 0.5; d->gear1_weight_on_wheels = gear_property(fdm, 1, "WOW") > 0.5; d->gear2_weight_on_wheels = gear_property(fdm, 2, "WOW") > 0.5;
    d->gear0_steer_type = ground->GetNumGearUnits() > 0 ? ground->GetGearUnit(0)->GetSteerType() : -1;
    d->gear0_steerable = ground->GetNumGearUnits() > 0 && ground->GetGearUnit(0)->GetSteerable() ? 1 : 0;
    return 1;
  } catch (...) { return catch_failure(native, "get_diagnostics"); }
}

const char* qfl_jsbsim_last_error(qfl_jsbsim_handle handle) { NativeInstance* native = instance(handle); return native ? native->last_error.c_str() : g_last_error.c_str(); }
}
