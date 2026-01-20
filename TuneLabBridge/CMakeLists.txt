# TuneLab Bridge VST3 Plugin
# Built with JUCE Framework
cmake_minimum_required(VERSION 3.22)

project(TuneLabBridge VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Fetch JUCE from GitHub
include(FetchContent)
FetchContent_Declare(
    JUCE
    GIT_REPOSITORY https://github.com/juce-framework/JUCE.git
    GIT_TAG 8.0.4
    GIT_SHALLOW TRUE
)
FetchContent_MakeAvailable(JUCE)

# Plugin target
juce_add_plugin(TuneLabBridge
    COMPANY_NAME "TuneLab"
    COMPANY_COPYRIGHT "Copyright (c) 2024 TuneLab"
    COMPANY_WEBSITE "https://tunelab.app"
    COMPANY_EMAIL "support@tunelab.app"
    PLUGIN_MANUFACTURER_CODE Tune
    PLUGIN_CODE TlBr
    FORMATS VST3
    PRODUCT_NAME "TuneLab Bridge"
    DESCRIPTION "Bridge plugin to receive audio from TuneLab"
    IS_SYNTH TRUE
    NEEDS_MIDI_INPUT FALSE
    NEEDS_MIDI_OUTPUT FALSE
    IS_MIDI_EFFECT FALSE
    EDITOR_WANTS_KEYBOARD_FOCUS FALSE
    VST3_CATEGORIES "Instrument" "Generator"
)

# Source files
# Generate JuceHeader.h
juce_generate_juce_header(TuneLabBridge)

target_sources(TuneLabBridge
    PRIVATE
        Source/PluginProcessor.cpp
        Source/PluginProcessor.h
        Source/PluginEditor.cpp
        Source/PluginEditor.h
        Source/IPC/IPCClient.cpp
        Source/IPC/IPCClient.h
        Source/IPC/SharedMemoryClient.cpp
        Source/IPC/SharedMemoryClient.h
        Source/IPC/NamedPipeClient.cpp
        Source/IPC/NamedPipeClient.h
        Source/IPC/RingBuffer.h
        Source/Model/BridgeProtocol.h
        Source/Model/TrackInfo.h
        Source/Model/TransportState.h
        Source/UI/TrackListComponent.cpp
        Source/UI/TrackListComponent.h
        Source/UI/StatusBar.cpp
        Source/UI/StatusBar.h
)

target_compile_features(TuneLabBridge PUBLIC cxx_std_17)

target_compile_definitions(TuneLabBridge
    PUBLIC
        JUCE_VST3_CAN_REPLACE_VST2=0
        JUCE_WEB_BROWSER=0
        JUCE_USE_CURL=0
        JUCE_DISPLAY_SPLASH_SCREEN=0
    PRIVATE
        NOMINMAX    # Prevent Windows min/max macros
)

target_link_libraries(TuneLabBridge
    PRIVATE
        juce::juce_audio_utils
        juce::juce_core
        juce::juce_gui_basics
    PUBLIC
        juce::juce_recommended_config_flags
        juce::juce_recommended_warning_flags
)

# Platform-specific libraries
if(WIN32)
    target_link_libraries(TuneLabBridge PRIVATE kernel32)
elseif(APPLE)
    # macOS: no additional libraries needed for shared memory
elseif(UNIX)
    target_link_libraries(TuneLabBridge PRIVATE rt pthread)
endif()
