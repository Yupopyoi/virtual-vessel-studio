# The capture native plugin (capture detailed design 8.2, 16): DXGI Desktop Duplication for
# SubScreenCapture. Built with tools/native/build-capture.ps1.

cmake_minimum_required(VERSION 3.21)

project(VirtualVesselCaptureNative LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The capture plugin uses DXGI Desktop Duplication and builds only on Windows.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# The C++ runtime is linked statically, so the plugin needs no Visual C++ redistributable.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_library(vv_capture_core STATIC
    src/capture_session.cpp
    src/desktop_duplication.cpp
    src/frame_exchange.cpp)
target_include_directories(vv_capture_core PUBLIC include src)
target_compile_definitions(vv_capture_core PUBLIC UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(vv_capture_core PUBLIC /W4 /WX /permissive- /utf-8 /external:anglebrackets /external:W0)
target_link_libraries(vv_capture_core PUBLIC d3d11 dxgi user32)

add_library(VirtualVessel.Capture.Native SHARED src/exports.cpp)
target_compile_definitions(VirtualVessel.Capture.Native PRIVATE VV_CAPTURE_EXPORTS)
target_link_libraries(VirtualVessel.Capture.Native PRIVATE vv_capture_core)

enable_testing()
add_executable(vv_capture_tests tests/capture_tests.cpp)
target_link_libraries(vv_capture_tests PRIVATE vv_capture_core VirtualVessel.Capture.Native)
add_test(NAME vv_capture_tests COMMAND vv_capture_tests)
