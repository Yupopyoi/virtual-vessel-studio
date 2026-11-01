# The audio native plugin (audio detailed design): WASAPI capture, the mix, and the stream tap.
# Built with tools/native/build-audio.ps1.

cmake_minimum_required(VERSION 3.21)

project(VirtualVesselAudioNative LANGUAGES C CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The audio plugin uses WASAPI and builds only on Windows.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# The C++ runtime is linked statically, so the plugin needs no Visual C++ redistributable.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# RNNoise, fetched by tools/deps/fetch-rnnoise.ps1 at a fixed commit. Its warnings are not ours to fix.
set(RNNOISE_DIR ${CMAKE_CURRENT_SOURCE_DIR}/third_party/rnnoise)
add_library(rnnoise STATIC
    ${RNNOISE_DIR}/src/denoise.c
    ${RNNOISE_DIR}/src/rnn.c
    ${RNNOISE_DIR}/src/pitch.c
    ${RNNOISE_DIR}/src/kiss_fft.c
    ${RNNOISE_DIR}/src/celt_lpc.c
    ${RNNOISE_DIR}/src/nnet.c
    ${RNNOISE_DIR}/src/nnet_default.c
    ${RNNOISE_DIR}/src/parse_lpcnet_weights.c
    ${RNNOISE_DIR}/src/rnnoise_data.c
    ${RNNOISE_DIR}/src/rnnoise_tables.c)
target_include_directories(rnnoise PUBLIC ${RNNOISE_DIR}/include PRIVATE ${RNNOISE_DIR}/src)
target_compile_definitions(rnnoise PRIVATE _USE_MATH_DEFINES _CRT_SECURE_NO_WARNINGS)
target_compile_options(rnnoise PRIVATE /W0)

add_library(vv_audio_core STATIC
    src/mixer.cpp
    src/denoiser.cpp
    src/device_loop.cpp
    src/device_output.cpp
    src/device_source.cpp)
target_include_directories(vv_audio_core PUBLIC include src)
target_compile_definitions(vv_audio_core PUBLIC UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(vv_audio_core PUBLIC /W4 /WX /permissive- /utf-8 /external:anglebrackets /external:W0)
target_link_libraries(vv_audio_core PUBLIC ole32 avrt PRIVATE rnnoise)

add_library(VirtualVessel.Audio.Native SHARED src/exports.cpp)
target_compile_definitions(VirtualVessel.Audio.Native PRIVATE VV_AUDIO_EXPORTS)
target_link_libraries(VirtualVessel.Audio.Native PRIVATE vv_audio_core)

enable_testing()
add_executable(vv_audio_tests tests/audio_tests.cpp)
target_link_libraries(vv_audio_tests PRIVATE vv_audio_core VirtualVessel.Audio.Native)
add_test(NAME vv_audio_tests COMMAND vv_audio_tests)
