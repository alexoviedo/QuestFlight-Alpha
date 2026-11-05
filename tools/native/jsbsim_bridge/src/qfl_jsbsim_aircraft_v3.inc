// Included once by qfl_jsbsim_native.cpp; shares its private native instance.
static_assert(sizeof(int) == 4, "ABI requires 32-bit int");
static_assert(sizeof(qfl_jsbsim_capabilities_v3) == 32, "Capability ABI");
static_assert(sizeof(qfl_jsbsim_station_load_v3) == 16 && offsetof(qfl_jsbsim_station_load_v3, pounds) == 8, "Loading ABI");
static_assert(sizeof(qfl_jsbsim_attachment_state_v3) == 144, "Attachment ABI");
static_assert(sizeof(qfl_jsbsim_external_load_v3) == 32, "Load ABI");

extern "C" {
int qfl_jsbsim_set_terrain_patch_v3(qfl_jsbsim_handle h,const qfl_jsbsim_terrain_patch_v3* p,const double* heights,int count) {
  auto n=instance(h);if(!require_loaded(n)||!p||!heights)return 0;
  if(p->rows<2||p->rows>33||p->columns<2||p->columns>33||count!=p->rows*p->columns||
     !std::isfinite(p->minimum_latitude_degrees)||!std::isfinite(p->minimum_longitude_degrees)||
     !std::isfinite(p->latitude_step_degrees)||!std::isfinite(p->longitude_step_degrees)||
     p->minimum_latitude_degrees< -89||p->minimum_latitude_degrees+(p->rows-1)*p->latitude_step_degrees>89||
     p->minimum_longitude_degrees< -180||p->minimum_longitude_degrees+(p->columns-1)*p->longitude_step_degrees>180||
     p->latitude_step_degrees<=0||p->longitude_step_degrees<=0||p->latitude_step_degrees>.01||p->longitude_step_degrees>.01) {
    set_error(n,"Invalid bounded terrain patch.");return 0;
  }
  for(int i=0;i<count;++i)if(!std::isfinite(heights[i])||heights[i]< -1000||heights[i]>10000){set_error(n,"Invalid terrain elevation.");return 0;}
  try {
    if(!n->prepared_ground){
      auto ground=std::make_unique<PreparedGround>();
      ground->SetEllipse(n->fdm->GetInertial()->GetSemimajor(),n->fdm->GetInertial()->GetSemiminor());
      n->prepared_ground=ground.get();n->fdm->GetInertial()->SetGroundCallback(ground.release());
    }
    n->prepared_ground->patch=*p;std::copy(heights,heights+count,n->prepared_ground->heights.begin());
    n->prepared_ground->outside=false;return 1;
  }catch(...){return catch_failure(n,"set_terrain_patch_v3");}
}

int qfl_jsbsim_get_capabilities_v3(qfl_jsbsim_handle h, qfl_jsbsim_capabilities_v3* out) {
  auto n = instance(h); if (!require_loaded(n) || !out) return 0;
  *out = n->capabilities; return 1;
}

int qfl_jsbsim_set_loading_v3(qfl_jsbsim_handle h, const qfl_jsbsim_station_load_v3* stations, int station_count,
                            const qfl_jsbsim_station_load_v3* tanks, int tank_count) {
  auto n = instance(h); if (!require_loaded(n)) return 0;
  auto validate = [](const qfl_jsbsim_station_load_v3* loads, int count, int capacity) {
    if (count < 0 || count > capacity || (count && !loads)) return false;
    unsigned int seen = 0;
    for (int i=0; i<count; ++i) {
      if (loads[i].index < 0 || loads[i].index >= capacity || !std::isfinite(loads[i].pounds) || loads[i].pounds < 0 || loads[i].pounds > 10000) return false;
      unsigned int mask = 1u << loads[i].index;
      if (seen & mask) return false;
      seen |= mask;
    }
    return true;
  };
  if (!validate(stations,station_count,n->capabilities.station_count) || !validate(tanks,tank_count,n->capabilities.tank_count)) {
    set_error(n,"Invalid, duplicate, unsupported or nonfinite loading station."); return 0;
  }
  try {
    std::array<double,8> previous_stations{};std::array<double,4> previous_tanks{};
    for(int i=0;i<n->capabilities.station_count;++i)previous_stations[i]=n->stations[i]->getDoubleValue();
    for(int i=0;i<n->capabilities.tank_count;++i)previous_tanks[i]=n->tanks[i]->getDoubleValue();
    for (int i=0;i<station_count;++i) n->stations[stations[i].index]->setDoubleValue(stations[i].pounds);
    for (int i=0;i<tank_count;++i) n->tanks[tanks[i].index]->setDoubleValue(tanks[i].pounds);
    recompute_loading(*n);
    if(!valid_mass(*n)) {
      for(int i=0;i<n->capabilities.station_count;++i)n->stations[i]->setDoubleValue(previous_stations[i]);
      for(int i=0;i<n->capabilities.tank_count;++i)n->tanks[i]->setDoubleValue(previous_tanks[i]);
      recompute_loading(*n);set_error(n,"Loading rejected: mass, CG or positive-definite inertia outside declared envelope.");return 0;
    }
    return 1;
  } catch (...) { return catch_failure(n,"set_loading_v3"); }
}

int qfl_jsbsim_set_glider_controls_v3(qfl_jsbsim_handle h, const qfl_jsbsim_glider_controls_v3* c) {
  auto n=instance(h); if (!require_loaded(n) || !c) return 0;
  if (!n->capabilities.has_airbrake || !std::isfinite(c->airbrake) || !std::isfinite(c->wheel_brake) ||
      c->airbrake<0 || c->airbrake>1 || c->wheel_brake<0 || c->wheel_brake>1) { set_error(n,"Invalid or unsupported glider controls."); return 0; }
  try {
    n->airbrake_command->setDoubleValue(c->airbrake);
    cached_set(*n,HotProperty::FcsCenterBrakeCmdNorm,c->wheel_brake);
    return 1;
  } catch (...) { return catch_failure(n,"set_glider_controls_v3"); }
}

int qfl_jsbsim_clear_external_loads_v3(qfl_jsbsim_handle h) {
  auto n=instance(h); if (!require_loaded(n)) return 0;
  try { for (auto& channel:n->external) if (channel[3]) channel[3]->setDoubleValue(0); return 1; }
  catch (...) { return catch_failure(n,"clear_external_loads_v3"); }
}

int qfl_jsbsim_set_external_load_v3(qfl_jsbsim_handle h,const qfl_jsbsim_external_load_v3* load) {
  auto n=instance(h); if (!require_loaded(n) || !load) return 0;
  if (load->channel<0 || load->channel>2 || !n->external[load->channel][3] ||
      !std::isfinite(load->body_x_si) || !std::isfinite(load->body_y_si) || !std::isfinite(load->body_z_si)) {
    qfl_jsbsim_clear_external_loads_v3(h); set_error(n,"Unsupported channel or nonfinite external load."); return 0;
  }
  try {
    auto& nodes=n->external[load->channel];
    const double magnitude=std::hypot(load->body_x_si,load->body_y_si,load->body_z_si);
    if (!std::isfinite(magnitude) || magnitude>1000000) { qfl_jsbsim_clear_external_loads_v3(h); set_error(n,"External load exceeds bounded SI range."); return 0; }
    nodes[0]->setDoubleValue(magnitude>0 ? load->body_x_si/magnitude : 1);
    nodes[1]->setDoubleValue(magnitude>0 ? load->body_y_si/magnitude : 0);
    nodes[2]->setDoubleValue(magnitude>0 ? load->body_z_si/magnitude : 0);
    nodes[3]->setDoubleValue(magnitude/(load->channel==2 ? 1.3558179483314004 : 4.4482216152605));
    if (load->channel==1) {
      auto cg=n->fdm->GetMassBalance()->GetXYZcg();
      for (int i=0;i<3;++i) nodes[4+i]->setDoubleValue(cg(i+1));
    }
    return 1;
  } catch (...) { qfl_jsbsim_clear_external_loads_v3(h); return catch_failure(n,"set_external_load_v3"); }
}

int qfl_jsbsim_get_attachment_state_v3(qfl_jsbsim_handle h,int id,qfl_jsbsim_attachment_state_v3* out) {
  auto n=instance(h); if (!require_loaded(n) || !out) return 0;
  if (id!=0 || !n->capabilities.has_tow_force) { set_error(n,"Tow attachment is unavailable."); return 0; }
  try {
    const auto& nodes=n->external[0];
    JSBSim::FGColumnVector3 structural(nodes[4]->getDoubleValue(),nodes[5]->getDoubleValue(),nodes[6]->getDoubleValue());
    auto offset=n->fdm->GetMassBalance()->StructuralToBody(structural); // feet, FRD
    auto p=n->fdm->GetPropagate(); const auto& matrix=p->GetTb2ec();
    auto relative=matrix*offset;
    // UVW and PQR are relative to rotating Earth in the pinned API. PQRi
    // would count Earth's rotation twice here. Vector product is right handed.
    auto velocity=matrix*(p->GetUVW()+p->GetPQR()*offset);
    out->position_ecef_x_m=(p->GetLocation(1)+relative(1))*.3048;
    out->position_ecef_y_m=(p->GetLocation(2)+relative(2))*.3048;
    out->position_ecef_z_m=(p->GetLocation(3)+relative(3))*.3048;
    out->velocity_ecef_x_mps=velocity(1)*.3048;out->velocity_ecef_y_mps=velocity(2)*.3048;out->velocity_ecef_z_mps=velocity(3)*.3048;
    out->body_offset_x_m=offset(1)*.3048;out->body_offset_y_m=offset(2)*.3048;out->body_offset_z_m=offset(3)*.3048;
    out->body_to_ecef_11=matrix(1,1);out->body_to_ecef_12=matrix(1,2);out->body_to_ecef_13=matrix(1,3);
    out->body_to_ecef_21=matrix(2,1);out->body_to_ecef_22=matrix(2,2);out->body_to_ecef_23=matrix(2,3);
    out->body_to_ecef_31=matrix(3,1);out->body_to_ecef_32=matrix(3,2);out->body_to_ecef_33=matrix(3,3);
    return 1;
  } catch (...) { return catch_failure(n,"get_attachment_state_v3"); }
}

int qfl_jsbsim_set_conditions_v3(qfl_jsbsim_handle h,const qfl_jsbsim_conditions_v3* c) {
  auto n=instance(h);if(!require_loaded(n)||!c)return 0;
  if(!std::isfinite(c->sea_level_pressure_pa)||!std::isfinite(c->isa_temperature_offset_kelvin)||
     c->sea_level_pressure_pa<80000||c->sea_level_pressure_pa>110000||c->isa_temperature_offset_kelvin< -60||c->isa_temperature_offset_kelvin>60) {
    set_error(n,"Atmosphere reference is outside supported pressure/temperature bounds.");return 0;
  }
  try {
    auto atmosphere=std::dynamic_pointer_cast<JSBSim::FGStandardAtmosphere>(n->fdm->GetAtmosphere());
    if(!atmosphere){set_error(n,"Standard atmosphere is required.");return 0;}
    atmosphere->SetPressureSL(JSBSim::FGAtmosphere::ePascals,c->sea_level_pressure_pa);
    atmosphere->SetTemperatureBias(JSBSim::FGAtmosphere::eKelvin,c->isa_temperature_offset_kelvin);
    return 1;
  }catch(...){return catch_failure(n,"set_conditions_v3");}
}

int qfl_jsbsim_get_air_data_v3(qfl_jsbsim_handle h,qfl_jsbsim_air_data_v3* out) {
  auto n=instance(h);if(!require_loaded(n)||!out)return 0;
  try {
    auto a=n->fdm->GetAtmosphere();auto air=n->fdm->GetAuxiliary();
    out->density_kg_m3=a->GetDensity()*515.3788183931961;
    out->pressure_pa=a->GetPressure()*47.88025898033584;out->temperature_kelvin=a->GetTemperature()*5/9;
    out->true_airspeed_mps=air->GetVtrueFPS()*.3048;out->equivalent_airspeed_mps=air->GetVequivalentKTS()*.5144444444444445;
    out->calibrated_airspeed_mps=air->GetVcalibratedKTS()*.5144444444444445;
    out->pressure_altitude_m=a->GetPressureAltitude()*.3048;
    out->airbrake_position=n->capabilities.has_airbrake?n->airbrake_position->getDoubleValue():0;
    return 1;
  }catch(...){return catch_failure(n,"get_air_data_v3");}
}

int qfl_jsbsim_get_contact_v3(qfl_jsbsim_handle h,int id,qfl_jsbsim_contact_v3* out) {
  auto n=instance(h);if(!require_loaded(n)||!out)return 0;
  if(id<0||id>=n->capabilities.contact_count){set_error(n,"Contact index out of range.");return 0;}
  try {
    auto gear=n->fdm->GetGroundReactions()->GetGearUnit(id);
    out->compression_ft=compression(*n,id);
    out->force_body_x_n=gear->GetBodyXForce()*4.4482216152605;out->force_body_y_n=gear->GetBodyYForce()*4.4482216152605;out->force_body_z_n=gear->GetBodyZForce()*4.4482216152605;
    out->weight_on_wheels=gear->GetWOW()?1:0;out->steer_type=gear->GetSteerType();return 1;
  }catch(...){return catch_failure(n,"get_contact_v3");}
}
}
