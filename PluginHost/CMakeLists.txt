cmake_minimum_required(VERSION 3.22)

project(PluginHost VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Find JUCE - Use FetchContent to download JUCE
include(FetchContent)

FetchContent_Declare(
    JUCE
    GIT_REPOSITORY https://github.com/juce-framework/JUCE.git
    GIT_TAG 8.0.4
    GIT_SHALLOW TRUE
)

FetchContent_MakeAvailable(JUCE)

# Create the shared library
add_library(PluginHost SHARED
    src/VstHost.cpp
    src/VstHostApi.cpp
    src/PluginInstance.cpp
    src/AudioProcessor.cpp
    src/MidiProcessor.cpp
)

# Link JUCE modules
target_link_libraries(PluginHost
    PRIVATE
        juce::juce_audio_basics
        juce::juce_audio_devices
        juce::juce_audio_formats
        juce::juce_audio_processors
        juce::juce_audio_utils
        juce::juce_core
        juce::juce_data_structures
        juce::juce_events
        juce::juce_graphics
        juce::juce_gui_basics
        juce::juce_gui_extra
    PUBLIC
        juce::juce_recommended_config_flags
        juce::juce_recommended_warning_flags
)

# Add JUCE definitions
target_compile_definitions(PluginHost
    PUBLIC
        JUCE_PLUGINHOST_VST3=1
        JUCE_PLUGINHOST_VST=1
        JUCE_PLUGINHOST_AU=1
        JUCE_PLUGINHOST_LADSPA=1
        JUCE_WEB_BROWSER=0
        JUCE_USE_CURL=0
        JUCE_VST3_CAN_REPLACE_VST2=0
        JUCE_DISPLAY_SPLASH_SCREEN=0
        JUCE_REPORT_APP_USAGE=0
)

# Platform-specific settings
if(WIN32)
    target_compile_definitions(PluginHost PRIVATE PLUGIN_HOST_WINDOWS=1)
    set_target_properties(PluginHost PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    )
elseif(APPLE)
    target_compile_definitions(PluginHost PRIVATE PLUGIN_HOST_MAC=1)
    set_target_properties(PluginHost PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    )
elseif(UNIX)
    target_compile_definitions(PluginHost PRIVATE PLUGIN_HOST_LINUX=1)
    set_target_properties(PluginHost PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    )
endif()

# Include directories
target_include_directories(PluginHost
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

# Export symbols for DLL
if(WIN32)
    target_compile_definitions(PluginHost PRIVATE PLUGIN_HOST_EXPORTS)
endif()

# Install targets
install(TARGETS PluginHost
    LIBRARY DESTINATION lib
    RUNTIME DESTINATION bin
)

install(FILES include/PluginHostApi.h DESTINATION include)
