#ifndef QFL_JSBSIM_NATIVE_H
#define QFL_JSBSIM_NATIVE_H

#if defined(_WIN32)
#  if defined(QFL_JSBSIM_NATIVE_BUILD)
#    define QFL_JSBSIM_API __declspec(dllexport)
#  else
#    define QFL_JSBSIM_API __declspec(dllimport)
#  endif
#else
#  define QFL_JSBSIM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef void* qfl_jsbsim_handle;

/* ABI 3 additions. All legacy layouts below remain unchanged. int is checked
   as 32 bits on both supported targets; doubles use natural 8-byte alignment. */
typedef struct qfl_jsbsim_capabilities_v3 {
  int engine_count, tank_count, station_count, contact_count;
  int has_airbrake, has_tow_force, has_airflow_force, has_airflow_moment;
} qfl_jsbsim_capabilities_v3;

typedef struct qfl_jsbsim_station_load_v3 {
  int index;
  double pounds;
} qfl_jsbsim_station_load_v3;

typedef struct qfl_jsbsim_glider_controls_v3 {
  double airbrake, wheel_brake;
} qfl_jsbsim_glider_controls_v3;

/* channel 0: hook force, 1: incremental airflow force at loaded CG,
   2: pure airflow moment. Body FRD newtons or newton meters respectively. */
typedef struct qfl_jsbsim_external_load_v3 {
  int channel;
  double body_x_si, body_y_si, body_z_si;
} qfl_jsbsim_external_load_v3;

typedef struct qfl_jsbsim_attachment_state_v3 {
  double position_ecef_x_m, position_ecef_y_m, position_ecef_z_m;
  double velocity_ecef_x_mps, velocity_ecef_y_mps, velocity_ecef_z_mps;
  double body_offset_x_m, body_offset_y_m, body_offset_z_m;
  double body_to_ecef_11, body_to_ecef_12, body_to_ecef_13;
  double body_to_ecef_21, body_to_ecef_22, body_to_ecef_23;
  double body_to_ecef_31, body_to_ecef_32, body_to_ecef_33;
} qfl_jsbsim_attachment_state_v3;

typedef struct qfl_jsbsim_conditions_v3 {
  double sea_level_pressure_pa, isa_temperature_offset_kelvin;
} qfl_jsbsim_conditions_v3;

typedef struct qfl_jsbsim_air_data_v3 {
  double density_kg_m3, pressure_pa, temperature_kelvin;
  double true_airspeed_mps, equivalent_airspeed_mps, calibrated_airspeed_mps;
  double pressure_altitude_m, airbrake_position;
} qfl_jsbsim_air_data_v3;

typedef struct qfl_jsbsim_contact_v3 {
  double compression_ft, force_body_x_n, force_body_y_n, force_body_z_n;
  int weight_on_wheels, steer_type;
} qfl_jsbsim_contact_v3;

/* Immutable bounded geodetic height patch, row-major north/east. Heights are
   geodetic MSL meters. Prepared by the existing authored terrain sampler. */
typedef struct qfl_jsbsim_terrain_patch_v3 {
  double minimum_latitude_degrees, minimum_longitude_degrees;
  double latitude_step_degrees, longitude_step_degrees;
  int rows, columns;
} qfl_jsbsim_terrain_patch_v3;
QFL_JSBSIM_API int qfl_jsbsim_set_terrain_patch_v3(qfl_jsbsim_handle, const qfl_jsbsim_terrain_patch_v3*, const double*, int);

QFL_JSBSIM_API int qfl_jsbsim_get_capabilities_v3(qfl_jsbsim_handle, qfl_jsbsim_capabilities_v3*);
QFL_JSBSIM_API int qfl_jsbsim_set_loading_v3(qfl_jsbsim_handle, const qfl_jsbsim_station_load_v3*, int, const qfl_jsbsim_station_load_v3*, int);
QFL_JSBSIM_API int qfl_jsbsim_set_glider_controls_v3(qfl_jsbsim_handle, const qfl_jsbsim_glider_controls_v3*);
QFL_JSBSIM_API int qfl_jsbsim_set_external_load_v3(qfl_jsbsim_handle, const qfl_jsbsim_external_load_v3*);
QFL_JSBSIM_API int qfl_jsbsim_clear_external_loads_v3(qfl_jsbsim_handle);
QFL_JSBSIM_API int qfl_jsbsim_get_attachment_state_v3(qfl_jsbsim_handle, int, qfl_jsbsim_attachment_state_v3*);
QFL_JSBSIM_API int qfl_jsbsim_set_conditions_v3(qfl_jsbsim_handle, const qfl_jsbsim_conditions_v3*);
QFL_JSBSIM_API int qfl_jsbsim_get_air_data_v3(qfl_jsbsim_handle, qfl_jsbsim_air_data_v3*);
QFL_JSBSIM_API int qfl_jsbsim_get_contact_v3(qfl_jsbsim_handle, int, qfl_jsbsim_contact_v3*);

typedef struct qfl_jsbsim_initial_conditions {
  double latitude_degrees, longitude_degrees, altitude_msl_feet;
  double terrain_elevation_msl_feet, calibrated_airspeed_knots;
  double heading_degrees, pitch_degrees, bank_degrees, flight_path_angle_degrees;
  int engine_running;
} qfl_jsbsim_initial_conditions;

/* Reset uses the last configured physical NED atmosphere. Positive CAS defines
   air-relative speed along heading/flight-path angle; zero CAS requests a
   stationary ground start whose reported airspeed still reflects the wind. */

typedef struct qfl_jsbsim_controls {
  double aileron, elevator, rudder, throttle, mixture;
  double carb_heat, elevator_trim, flaps, left_brake, right_brake;
} qfl_jsbsim_controls;

typedef struct qfl_jsbsim_atmosphere {
  double wind_north_feet_per_second, wind_east_feet_per_second, wind_down_feet_per_second;
} qfl_jsbsim_atmosphere;

typedef struct qfl_jsbsim_mass_configuration {
  double pilot_pounds, copilot_pounds, rear_left_pounds, rear_right_pounds;
  double baggage_pounds, left_fuel_pounds, right_fuel_pounds;
} qfl_jsbsim_mass_configuration;

typedef struct qfl_jsbsim_trim_request {
  int mode; /* 0=longitudinal, 1=full */
  int max_cycles;
  int max_cycles_per_axis;
  double tolerance;
} qfl_jsbsim_trim_request;

typedef struct qfl_jsbsim_trim_result {
  int success;
  double aileron_command, elevator_command, rudder_command, throttle_command;
  double pitch_trim_command, angle_of_attack_degrees, pitch_degrees, bank_degrees;
} qfl_jsbsim_trim_result;

typedef struct qfl_jsbsim_state {
  double simulation_time_seconds, latitude_degrees, longitude_degrees;
  double altitude_msl_feet, altitude_agl_feet, calibrated_airspeed_knots;
  double ground_speed_knots, vertical_speed_feet_per_minute, heading_degrees;
  double pitch_degrees, bank_degrees, angle_of_attack_degrees, sideslip_degrees;
  double velocity_north_feet_per_second, velocity_east_feet_per_second, velocity_down_feet_per_second;
  double roll_rate_radians_per_second, pitch_rate_radians_per_second, yaw_rate_radians_per_second;
  double engine_rpm, load_factor_g;
  int weight_on_wheels;
} qfl_jsbsim_state;

typedef struct qfl_jsbsim_diagnostics {
  double true_airspeed_knots, equivalent_airspeed_knots, density_slugs_per_cubic_foot;
  double terrain_elevation_msl_feet, weight_pounds, cg_x_inches, cg_y_inches, cg_z_inches;
  double left_fuel_pounds, right_fuel_pounds;
  double left_aileron_position, right_aileron_position, elevator_position, rudder_position, flap_position;
  double total_force_x_pounds, total_force_y_pounds, total_force_z_pounds;
  double aero_force_x_pounds, aero_force_y_pounds, aero_force_z_pounds;
  double propulsion_force_x_pounds, propulsion_force_y_pounds, propulsion_force_z_pounds;
  double gear_force_x_pounds, gear_force_y_pounds, gear_force_z_pounds;
  double total_roll_moment_pound_feet, total_pitch_moment_pound_feet, total_yaw_moment_pound_feet;
  double aero_roll_moment_pound_feet, aero_pitch_moment_pound_feet, aero_yaw_moment_pound_feet;
  double propulsion_roll_moment_pound_feet, propulsion_pitch_moment_pound_feet, propulsion_yaw_moment_pound_feet;
  double gear_roll_moment_pound_feet, gear_pitch_moment_pound_feet, gear_yaw_moment_pound_feet;
  double gear0_compression_feet, gear1_compression_feet, gear2_compression_feet;
  double gear0_steering_degrees, gear1_steering_degrees, gear2_steering_degrees;
  double steering_command;
  int gear0_weight_on_wheels, gear1_weight_on_wheels, gear2_weight_on_wheels;
  int gear0_steer_type, gear0_steerable;
} qfl_jsbsim_diagnostics;

QFL_JSBSIM_API int qfl_jsbsim_api_version(void);
QFL_JSBSIM_API qfl_jsbsim_handle qfl_jsbsim_create(void);
QFL_JSBSIM_API void qfl_jsbsim_destroy(qfl_jsbsim_handle handle);
QFL_JSBSIM_API int qfl_jsbsim_load_aircraft(qfl_jsbsim_handle handle, const char* data_root_utf8, const char* aircraft_name_utf8);
QFL_JSBSIM_API int qfl_jsbsim_set_mass_configuration(qfl_jsbsim_handle handle, const qfl_jsbsim_mass_configuration* configuration);
QFL_JSBSIM_API int qfl_jsbsim_reset(qfl_jsbsim_handle handle, const qfl_jsbsim_initial_conditions* initial_conditions);
QFL_JSBSIM_API int qfl_jsbsim_set_controls(qfl_jsbsim_handle handle, const qfl_jsbsim_controls* controls);
QFL_JSBSIM_API int qfl_jsbsim_set_atmosphere(qfl_jsbsim_handle handle, const qfl_jsbsim_atmosphere* atmosphere);
QFL_JSBSIM_API int qfl_jsbsim_set_terrain_elevation(qfl_jsbsim_handle handle, double terrain_elevation_msl_feet);
QFL_JSBSIM_API int qfl_jsbsim_trim(qfl_jsbsim_handle handle, const qfl_jsbsim_trim_request* request, qfl_jsbsim_trim_result* result);
QFL_JSBSIM_API int qfl_jsbsim_advance(qfl_jsbsim_handle handle, double fixed_delta_time_seconds);
QFL_JSBSIM_API int qfl_jsbsim_get_state(qfl_jsbsim_handle handle, qfl_jsbsim_state* state);
QFL_JSBSIM_API int qfl_jsbsim_get_diagnostics(qfl_jsbsim_handle handle, qfl_jsbsim_diagnostics* diagnostics);
QFL_JSBSIM_API const char* qfl_jsbsim_last_error(qfl_jsbsim_handle handle);

#ifdef __cplusplus
}
#endif
#endif
