cmake_minimum_required(VERSION 3.18)
project(qfl_jsbsim_native LANGUAGES CXX)

if(NOT DEFINED JSBSIM_SOURCE_DIR)
  message(FATAL_ERROR "JSBSIM_SOURCE_DIR must point to JSBSim revision 3b25f25e49b42d0489c04ac805674fc1450ca579")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(BUILD_SHARED_LIBS ON CACHE BOOL "Build replaceable LGPL JSBSim shared library" FORCE)
set(BUILD_DOCS OFF CACHE BOOL "" FORCE)
set(BUILD_PYTHON_MODULE OFF CACHE BOOL "" FORCE)
set(BUILD_JULIA_PACKAGE OFF CACHE BOOL "" FORCE)
set(BUILD_MATLAB_SFUNCTION OFF CACHE BOOL "" FORCE)
set(SYSTEM_EXPAT OFF CACHE BOOL "" FORCE)

if(ANDROID)
  add_compile_options("SHELL:-include arpa/inet.h")
endif()

add_subdirectory("${JSBSIM_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/jsbsim" EXCLUDE_FROM_ALL)

if(ANDROID)
  set_target_properties(libJSBSim PROPERTIES VERSION "" SOVERSION "")
  target_link_libraries(libJSBSim PUBLIC c++)
  target_link_options(libJSBSim PRIVATE
    "-Wl,-z,max-page-size=16384"
    "-Wl,-z,common-page-size=16384")
endif()

add_library(qfl_jsbsim_native SHARED src/qfl_jsbsim_native.cpp)
target_compile_definitions(qfl_jsbsim_native PRIVATE QFL_JSBSIM_NATIVE_BUILD=1)
target_include_directories(qfl_jsbsim_native PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include")
target_link_libraries(qfl_jsbsim_native PRIVATE libJSBSim)
set_target_properties(qfl_jsbsim_native PROPERTIES OUTPUT_NAME qfl_jsbsim_native)

if(ANDROID)
  target_link_options(qfl_jsbsim_native PRIVATE
    "-Wl,-z,max-page-size=16384"
    "-Wl,-z,common-page-size=16384")
endif()

if(NOT ANDROID)
  add_executable(qfl_jsbsim_smoke src/qfl_jsbsim_smoke.cpp)
  target_include_directories(qfl_jsbsim_smoke PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/include")
  target_link_libraries(qfl_jsbsim_smoke PRIVATE qfl_jsbsim_native)
endif()
