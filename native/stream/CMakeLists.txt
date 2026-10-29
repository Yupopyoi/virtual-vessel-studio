# The streaming native plugin (streaming detailed design): encoding with Media Foundation, FLV, RTMP, and TLS
# (SChannel). Built with tools/native/build-stream.ps1.

cmake_minimum_required(VERSION 3.21)

project(VirtualVesselStreamNative LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The streaming plugin uses Media Foundation and SChannel and builds only on Windows.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# The C++ runtime is linked statically, so the plugin needs no Visual C++ redistributable.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# The protocol and the send queue: no sockets or Windows APIs, so they are tested on their own.
add_library(vv_stream_protocol STATIC
    src/amf0.cpp
    src/flv.cpp
    src/rtmp_protocol.cpp
    src/send_queue.cpp)
target_include_directories(vv_stream_protocol PUBLIC src ../common/include)
target_compile_definitions(vv_stream_protocol PUBLIC UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(vv_stream_protocol PUBLIC /W4 /WX /permissive- /utf-8 /external:anglebrackets /external:W0)

enable_testing()
add_executable(vv_stream_protocol_tests tests/protocol_tests.cpp)
target_link_libraries(vv_stream_protocol_tests PRIVATE vv_stream_protocol)
add_test(NAME vv_stream_protocol_tests COMMAND vv_stream_protocol_tests)

# The connection: TCP, TLS (SChannel), and the RTMP publisher.
add_library(vv_stream_network STATIC
    src/transport.cpp
    src/tls_transport.cpp
    src/rtmp_client.cpp)
target_link_libraries(vv_stream_network PUBLIC vv_stream_protocol ws2_32 secur32)

add_executable(vv_stream_network_tests tests/network_tests.cpp)
target_link_libraries(vv_stream_network_tests PRIVATE vv_stream_network)
add_test(NAME vv_stream_network_tests COMMAND vv_stream_network_tests)

