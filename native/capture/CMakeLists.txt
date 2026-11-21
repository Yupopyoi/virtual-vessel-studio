# The capture native plugin (capture detailed design 8.2, 8.6, 16): DXGI Desktop Duplication for
# SubScreenCapture, and Windows.Graphics.Capture for a game's window on this PC. Built with
# tools/native/build-capture.ps1.

cmake_minimum_required(VERSION 3.21)

project(VirtualVesselCaptureNative LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The capture plugin uses DXGI Desktop Duplication and builds only on Windows.")
endif()

# C++20: C++/WinRT's coroutines then use <coroutine>; under C++17 they take <experimental/coroutine>, which
# newer MSVC (14.52 on CI) rejects.
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# The C++ runtime is linked statically, so the plugin needs no Visual C++ redistributable.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_library(vv_capture_core STATIC
    src/capture_session.cpp
    src/desktop_duplication.cpp
    src/frame_exchange.cpp
    src/session_core.cpp
    src/window_capture.cpp)
target_include_directories(vv_capture_core PUBLIC include src)
target_compile_definitions(vv_capture_core PUBLIC UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
# /bigobj: the C++/WinRT headers of Windows.Graphics.Capture make large objects.
target_compile_options(vv_capture_core PUBLIC /W4 /WX /permissive- /utf-8 /external:anglebrackets /external:W0 /bigobj)
target_link_libraries(vv_capture_core PUBLIC d3d11 dxgi user32 dwmapi windowsapp)

add_library(VirtualVessel.Capture.Native SHARED src/exports.cpp)
target_compile_definitions(VirtualVessel.Capture.Native PRIVATE VV_CAPTURE_EXPORTS)
target_link_libraries(VirtualVessel.Capture.Native PRIVATE vv_capture_core)

enable_testing()
add_executable(vv_capture_tests tests/capture_tests.cpp)
target_link_libraries(vv_capture_tests PRIVATE vv_capture_core VirtualVessel.Capture.Native)
add_test(NAME vv_capture_tests COMMAND vv_capture_tests)
