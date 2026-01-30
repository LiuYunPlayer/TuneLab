# PluginHost

A cross-platform VST/VST3/AU plugin hosting library built with JUCE, designed for use with TuneLab and other .NET applications.

## Features

- **Multi-format support**: Load VST2, VST3, AU (macOS), and LADSPA (Linux) plugins
- **Plugin scanning**: Discover installed plugins with async scanning
- **Audio processing**: Process audio through loaded plugins
- **MIDI support**: Send MIDI events to instrument and effect plugins
- **Parameter control**: Get and set plugin parameters
- **State management**: Save and restore plugin state/presets
- **Editor GUI**: Open plugin editor windows
- **Cross-platform**: Works on Windows, macOS, and Linux
- **C# bindings**: Complete P/Invoke bindings for .NET integration

## Project Structure

```
PluginHost/
├── include/                    # Public C API headers
│   └── PluginHostApi.h        # Main API header
├── src/                        # C++ implementation
│   ├── VstHost.h/.cpp         # Main host class
│   ├── PluginInstance.h/.cpp  # Plugin instance wrapper
│   ├── VstHostApi.cpp         # C API implementation
│   ├── AudioProcessor.cpp     # Audio utilities
│   └── MidiProcessor.cpp      # MIDI utilities
├── csharp/                     # C# bindings
│   └── PluginHost.Interop/    # .NET interop library
│       ├── NativeMethods.cs   # P/Invoke declarations
│       ├── PluginHostManager.cs # High-level manager
│       └── PluginInstance.cs  # Plugin instance wrapper
├── scripts/                    # Build scripts
│   ├── build-windows.bat      # Windows build script
│   └── build-unix.sh          # Linux/macOS build script
└── CMakeLists.txt             # CMake build configuration
```

## Building

### Prerequisites

- **CMake** 3.22 or later
- **C++ compiler** with C++17 support:
  - Windows: Visual Studio 2026 or later
  - macOS: Xcode 14 or later
  - Linux: GCC 9+ or Clang 10+
- **Git** (for fetching JUCE automatically)

### Windows

```batch
cd PluginHost\scripts
build-windows.bat
```

Or manually:

```batch
cd PluginHost
mkdir build
cd build
cmake -G "Visual Studio 18 2026" -A x64 ..
cmake --build . --config Release
```

### Linux / macOS

```bash
cd PluginHost/scripts
chmod +x build-unix.sh
./build-unix.sh
```

Or manually:

```bash
cd PluginHost
mkdir build && cd build
cmake -DCMAKE_BUILD_TYPE=Release ..
cmake --build .
```

### Output

- Windows: `build/bin/Release/PluginHost.dll`
- Linux: `build/bin/libPluginHost.so`
- macOS: `build/bin/libPluginHost.dylib`

## Usage

### C# (.NET)

Add a reference to `PluginHost.Interop` and copy the native library to your output directory.

```csharp
using PluginHost.Interop;

// Initialize the plugin host
var host = PluginHostManager.Instance;
host.Initialize();

// Add plugin search paths
host.AddScanPath(@"C:\Program Files\Common Files\VST3");

// Scan for plugins
host.ScanProgress += (s, e) => Console.WriteLine($"Scanning: {e.CurrentPath}");
host.ScanComplete += (s, e) => Console.WriteLine($"Found {e.TotalPluginsFound} plugins");
await host.ScanPluginsAsync();

// List discovered plugins
foreach (var info in host.GetAllPlugins())
{
    Console.WriteLine($"{info.Name} by {info.Vendor} ({info.Type})");
}

// Load a plugin
using var plugin = host.LoadPlugin(@"C:\path\to\plugin.vst3");

// Configure audio
plugin.SetAudioConfig(
    sampleRate: 44100,
    blockSize: 512,
    numInputChannels: 2,
    numOutputChannels: 2
);

// Prepare for playback
plugin.PrepareToPlay();

// Process audio
float[] inputBuffer = new float[512 * 2];  // Interleaved stereo
float[] outputBuffer = new float[512 * 2];

// For synth plugins, send MIDI notes
if (plugin.IsSynth)
{
    plugin.SendNoteOn(channel: 0, note: 60, velocity: 100);
}

// Process
plugin.ProcessAudioInterleaved(inputBuffer, outputBuffer, 2, 2, 512);

// Release note
if (plugin.IsSynth)
{
    plugin.SendNoteOff(channel: 0, note: 60);
}

// Save plugin state
byte[] state = plugin.GetState();

// Restore plugin state
plugin.SetState(state);

// Open editor (pass native window handle)
if (plugin.HasEditor)
{
    var (width, height) = plugin.GetEditorSize();
    // Create a window with the appropriate size, then:
    // plugin.OpenEditor(windowHandle);
}

// Cleanup
plugin.ReleaseResources();

// Shutdown
host.Shutdown();
```

### C/C++

Include the header and link against the library:

```c
#include <PluginHostApi.h>

int main()
{
    // Initialize
    PluginHost_Initialize();
    
    // Add scan paths
    PluginHost_AddScanPath("C:\\Program Files\\Common Files\\VST3");
    
    // Scan (blocking example)
    PluginHost_StartScan(NULL, NULL, NULL);
    while (PluginHost_IsScanning())
    {
        // Wait...
    }
    
    // Load plugin
    PluginInstanceHandle plugin;
    PluginHost_LoadPlugin("C:\\path\\to\\plugin.vst3", &plugin);
    
    // Configure audio
    AudioConfig config = {
        .sampleRate = 44100.0,
        .blockSize = 512,
        .numInputChannels = 2,
        .numOutputChannels = 2
    };
    PluginHost_SetAudioConfig(plugin, &config);
    
    // Prepare
    PluginHost_PrepareToPlay(plugin);
    
    // Process audio
    float inputBuffer[1024];   // 512 samples * 2 channels
    float outputBuffer[1024];
    
    PluginHost_ProcessAudioInterleaved(
        plugin,
        inputBuffer,
        outputBuffer,
        2, 2, 512
    );
    
    // Cleanup
    PluginHost_ReleaseResources(plugin);
    PluginHost_UnloadPlugin(plugin);
    PluginHost_Shutdown();
    
    return 0;
}
```

## Integration with TuneLab

To use PluginHost with TuneLab:

1. Build the native library for your platform
2. Add a reference to `PluginHost.Interop.csproj` in TuneLab:

```xml
<ProjectReference Include="..\PluginHost\csharp\PluginHost.Interop\PluginHost.Interop.csproj" />
```

3. Copy the native library to TuneLab's output directory:
   - Add a post-build step or include in the project file
   - The library should be in the same directory as the executable

4. Use the `PluginHostManager` to load and use VST plugins for synthesis

## API Reference

### PluginHostManager

| Method | Description |
|--------|-------------|
| `Initialize()` | Initialize the plugin host system |
| `Shutdown()` | Shutdown and release all resources |
| `AddScanPath(path)` | Add a directory to scan for plugins |
| `RemoveScanPath(path)` | Remove a scan directory |
| `ScanPluginsAsync()` | Scan for plugins asynchronously |
| `GetAllPlugins()` | Get all discovered plugins |
| `LoadPlugin(path)` | Load a plugin from file path |
| `LoadPluginByUid(uid)` | Load a plugin by unique ID |

### PluginInstance

| Method | Description |
|--------|-------------|
| `SetAudioConfig(...)` | Configure audio processing parameters |
| `PrepareToPlay()` | Prepare the plugin for processing |
| `ProcessAudioInterleaved(...)` | Process audio through the plugin |
| `SendNoteOn(...)` | Send MIDI note on |
| `SendNoteOff(...)` | Send MIDI note off |
| `GetParameter(index)` | Get parameter value (0-1) |
| `SetParameter(index, value)` | Set parameter value (0-1) |
| `GetState()` | Get plugin state as binary data |
| `SetState(data)` | Restore plugin state |
| `OpenEditor(hwnd)` | Open plugin editor GUI |
| `CloseEditor()` | Close plugin editor |

## License

This library is part of the TuneLab project.

## Third-Party

- **JUCE** - Cross-platform C++ framework (GPLv3 / Commercial License)
  https://juce.com/
