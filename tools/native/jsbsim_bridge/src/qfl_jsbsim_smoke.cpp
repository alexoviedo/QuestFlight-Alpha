#include "qfl_jsbsim_native.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <numeric>
#include <vector>

int main(int argc, char** argv) {
  if (argc != 2) { std::cerr << "usage: qfl_jsbsim_smoke <jsbsim-data-root>\n"; return 2; }
  qfl_jsbsim_handle h = qfl_jsbsim_create();
  if (!h || !qfl_jsbsim_load_aircraft(h, argv[1], "c172x")) { std::cerr << qfl_jsbsim_last_error(h) << "\n"; return 3; }
  qfl_jsbsim_mass_configuration mass{170, 170, 0, 0, 0, 130, 130};
  if (!qfl_jsbsim_set_mass_configuration(h, &mass)) { std::cerr << qfl_jsbsim_last_error(h) << "\n"; return 4; }
  qfl_jsbsim_initial_conditions ic{};
  ic.latitude_degrees=40.03936527; ic.longitude_degrees=-105.22608958; ic.altitude_msl_feet=6288.0;
  ic.terrain_elevation_msl_feet=5288.0; ic.calibrated_airspeed_knots=90.0; ic.heading_degrees=89.972; ic.engine_running=1;
  if (!qfl_jsbsim_reset(h, &ic)) { std::cerr << qfl_jsbsim_last_error(h) << "\n"; return 5; }
  qfl_jsbsim_trim_request tr{0, 60, 100, 0.001}; qfl_jsbsim_trim_result trim{};
  if (!qfl_jsbsim_trim(h, &tr, &trim)) { std::cerr << qfl_jsbsim_last_error(h) << "\n"; return 6; }
  qfl_jsbsim_controls controls{}; controls.aileron=trim.aileron_command; controls.elevator=trim.elevator_command;
  controls.rudder=trim.rudder_command; controls.throttle=trim.throttle_command; controls.elevator_trim=trim.pitch_trim_command; controls.mixture=1.0;
  constexpr double dt=1.0/120.0; constexpr int steps=2400; std::vector<double> timings; timings.reserve(steps);
  for (int i=0;i<steps;++i) { qfl_jsbsim_set_controls(h,&controls); auto a=std::chrono::steady_clock::now(); if(!qfl_jsbsim_advance(h,dt)){std::cerr<<qfl_jsbsim_last_error(h)<<"\n";return 7;} auto b=std::chrono::steady_clock::now(); timings.push_back(std::chrono::duration<double,std::milli>(b-a).count()); }
  qfl_jsbsim_state s{}; qfl_jsbsim_diagnostics d{};
  if(!qfl_jsbsim_get_state(h,&s)||!qfl_jsbsim_get_diagnostics(h,&d)){std::cerr<<qfl_jsbsim_last_error(h)<<"\n";return 8;}
  const double final_airspeed=s.calibrated_airspeed_knots, final_agl=s.altitude_agl_feet;
  const double weight=d.weight_pounds, cg_x=d.cg_x_inches;
  ic.altitude_msl_feet=ic.terrain_elevation_msl_feet+4.1; ic.calibrated_airspeed_knots=0.0;
  if(!qfl_jsbsim_reset(h,&ic)){std::cerr<<qfl_jsbsim_last_error(h)<<"\n";return 9;}
  controls={}; controls.rudder=0.8; controls.throttle=0.35; controls.mixture=1.0;
  if(!qfl_jsbsim_set_controls(h,&controls)||!qfl_jsbsim_get_diagnostics(h,&d)){std::cerr<<qfl_jsbsim_last_error(h)<<"\n";return 10;}
  const double pre_step_steer=d.gear0_steering_degrees, pre_step_command=d.steering_command;
  if(!qfl_jsbsim_advance(h,dt)||!qfl_jsbsim_get_diagnostics(h,&d)){std::cerr<<qfl_jsbsim_last_error(h)<<"\n";return 10;}
  const double nose_steer=d.gear0_steering_degrees, steer_command=d.steering_command;
  const int nose_steer_type=d.gear0_steer_type, nose_steerable=d.gear0_steerable;
  qfl_jsbsim_destroy(h); std::sort(timings.begin(),timings.end());
  double avg=std::accumulate(timings.begin(),timings.end(),0.0)/timings.size(); double p95=timings[(size_t)((timings.size()-1)*0.95)];
  bool finite=std::isfinite(final_agl)&&std::isfinite(weight)&&trim.success&&std::abs(nose_steer+8.0)<0.01;
  std::cout<<std::fixed<<std::setprecision(6)<<"{\"status\":\""<<(finite?"PASS":"FAIL")<<"\",\"api_version\":"<<qfl_jsbsim_api_version()<<",\"trim_success\":"<<trim.success<<",\"weight_lb\":"<<weight<<",\"cg_x_in\":"<<cg_x<<",\"pre_step_nose_steer_deg\":"<<pre_step_steer<<",\"pre_step_steer_command\":"<<pre_step_command<<",\"nose_steer_deg\":"<<nose_steer<<",\"steer_command\":"<<steer_command<<",\"nose_steer_type\":"<<nose_steer_type<<",\"nose_steerable\":"<<nose_steerable<<",\"average_step_ms\":"<<avg<<",\"p95_step_ms\":"<<p95<<",\"final_airspeed_kt\":"<<final_airspeed<<",\"final_agl_ft\":"<<final_agl<<"}\n";
  return finite?0:11;
}
