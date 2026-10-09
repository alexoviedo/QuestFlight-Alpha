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
