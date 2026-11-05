// Headless ARM64 ABI/model smoke, not APK graphics, cadence or thermal acceptance.
#include "qfl_jsbsim_native.h"
#include <array>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <stdexcept>
#include <string>

int main(int argc,char** argv) {
  if(argc!=2 || qfl_jsbsim_api_version()!=3)return 2;
  bool all=true;
  for(const char* model:{"c172x","qfl_sgs233_ref","qfl_pa18_tug_ref"}) {
    auto h=qfl_jsbsim_create();
    auto require=[&](bool ok){if(!ok)throw std::runtime_error(qfl_jsbsim_last_error(h));};
    try {
      require(h&&qfl_jsbsim_load_aircraft(h,argv[1],model));
      qfl_jsbsim_capabilities_v3 cap{};require(qfl_jsbsim_get_capabilities_v3(h,&cap));
      bool glider=std::string(model)=="qfl_sgs233_ref",tug=std::string(model)=="qfl_pa18_tug_ref";
      if(cap.engine_count!=(glider?0:1)||cap.tank_count!=(glider?0:2))throw std::runtime_error("Capability mismatch");
      if(glider||tug) {
        qfl_jsbsim_station_load_v3 stations[]={{0,glider?170.:0.},{1,170}},fuel[]={{0,60},{1,60}};
        require(qfl_jsbsim_set_loading_v3(h,stations,2,fuel,glider?0:2));
        std::array<double,1089> heights;heights.fill(5288*.3048);
        qfl_jsbsim_terrain_patch_v3 ground{40.03936527-.0128,-105.22608958-.0128,.0008,.0008,33,33};
        require(qfl_jsbsim_set_terrain_patch_v3(h,&ground,heights.data(),int(heights.size())));
      } else {
        qfl_jsbsim_mass_configuration mass{170,170,0,0,0,130,130};require(qfl_jsbsim_set_mass_configuration(h,&mass));
      }
      qfl_jsbsim_conditions_v3 conditions{101325,0};require(qfl_jsbsim_set_conditions_v3(h,&conditions));
      qfl_jsbsim_initial_conditions initial{};
      initial.latitude_degrees=40.03936527;initial.longitude_degrees=-105.22608958;
      initial.altitude_msl_feet=7288;initial.terrain_elevation_msl_feet=5288;
      initial.calibrated_airspeed_knots=glider?50:70;initial.heading_degrees=90;initial.engine_running=glider?0:1;
      require(qfl_jsbsim_reset(h,&initial));
      qfl_jsbsim_controls controls{};controls.throttle=glider?0:.65;controls.mixture=glider?0:1;controls.elevator_trim=glider?-.1062608793:0;
      require(qfl_jsbsim_set_controls(h,&controls));
      if(glider) {qfl_jsbsim_glider_controls_v3 c{.25,0};require(qfl_jsbsim_set_glider_controls_v3(h,&c));}
      if(glider||tug) {
        qfl_jsbsim_attachment_state_v3 hook{};require(qfl_jsbsim_get_attachment_state_v3(h,0,&hook));
        if(!std::isfinite(hook.position_ecef_x_m)||std::abs(hook.body_offset_x_m)<.1)throw std::runtime_error("Invalid hook");
        qfl_jsbsim_external_load_v3 force{0,100,0,0};require(qfl_jsbsim_set_external_load_v3(h,&force));
      }
      double totalMs=0;
      for(int i=0;i<600;i++) {
        auto before=std::chrono::steady_clock::now();require(qfl_jsbsim_advance(h,1./120));
        totalMs+=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-before).count();
      }
      qfl_jsbsim_state state{};qfl_jsbsim_diagnostics diagnostic{};qfl_jsbsim_air_data_v3 air{};
      require(qfl_jsbsim_get_state(h,&state));require(qfl_jsbsim_get_diagnostics(h,&diagnostic));require(qfl_jsbsim_get_air_data_v3(h,&air));
      if(!std::isfinite(state.altitude_msl_feet)||!std::isfinite(air.density_kg_m3)||std::abs(state.simulation_time_seconds-5)>1e-8||
         (glider&&(state.engine_rpm!=0||std::abs(diagnostic.weight_pounds-950)>.001||air.airbrake_position<.24)))throw std::runtime_error("Invalid final state");
      std::printf("{\"model\":\"%s\",\"passed\":true,\"seconds\":%.9f,\"weightLb\":%.6f,\"rpm\":%.6f,\"meanNativeStepMs\":%.6f}\n",model,state.simulation_time_seconds,diagnostic.weight_pounds,state.engine_rpm,totalMs/600);
    } catch(const std::exception& e) {std::fprintf(stderr,"%s: %s\n",model,e.what());all=false;}
    qfl_jsbsim_destroy(h);
  }
  std::printf("{\"allPassed\":%s,\"scope\":\"Headless native ABI3/model execution; not APK GPU or sustained Quest timing\"}\n",all?"true":"false");
  return all?0:1;
}
