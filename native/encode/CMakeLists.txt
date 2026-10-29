# The encoder native plugin (recording detailed design): H.264 or AV1 in MP4 through Media Foundation.
# Built with tools/native/build-encode.ps1.

cmake_minimum_required(VERSION 3.21)

project(VirtualVesselEncodeNative LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The encoder plugin uses Media Foundation and builds only on Windows.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_library(VirtualVessel.Encode.Native SHARED src/encoder.cpp)
target_include_directories(VirtualVessel.Encode.Native PUBLIC include)
target_compile_definitions(VirtualVessel.Encode.Native PRIVATE VV_ENCODE_EXPORTS UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(VirtualVessel.Encode.Native PRIVATE /W4 /WX /permissive- /utf-8 /external:anglebrackets /external:W0)
target_link_libraries(VirtualVessel.Encode.Native PRIVATE mfplat mfreadwrite mfuuid ole32)

enable_testing()
add_executable(vv_encode_tests tests/encode_tests.cpp)
target_include_directories(vv_encode_tests PRIVATE src)
target_compile_definitions(vv_encode_tests PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(vv_encode_tests PRIVATE VirtualVessel.Encode.Native mfplat mfreadwrite mfuuid ole32)
add_test(NAME vv_encode_tests COMMAND vv_encode_tests)
