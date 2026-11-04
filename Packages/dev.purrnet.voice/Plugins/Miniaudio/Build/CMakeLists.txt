cmake_minimum_required(VERSION 3.16)

project(PurrVoiceNativeAudio C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_library(PurrVoiceNativeAudio SHARED
    ../Source/miniaudio.c
    ../Source/purrvoice_miniaudio.c
)

target_include_directories(PurrVoiceNativeAudio PRIVATE ../Source)

if(WIN32)
    set_target_properties(PurrVoiceNativeAudio PROPERTIES OUTPUT_NAME "PurrVoiceNativeAudio")
elseif(APPLE)
    set_target_properties(PurrVoiceNativeAudio PROPERTIES OUTPUT_NAME "PurrVoiceNativeAudio")
    target_link_libraries(PurrVoiceNativeAudio PRIVATE
        "-framework CoreAudio"
        "-framework AudioToolbox"
        "-framework AudioUnit"
        "-framework CoreFoundation"
        "-framework CoreServices"
    )
elseif(UNIX)
    set_target_properties(PurrVoiceNativeAudio PROPERTIES OUTPUT_NAME "PurrVoiceNativeAudio")
    target_link_libraries(PurrVoiceNativeAudio PRIVATE dl pthread m)
endif()
