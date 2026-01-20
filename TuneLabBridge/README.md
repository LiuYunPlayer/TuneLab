# TuneLab Bridge VST3 Plugin

A VST3 instrument plugin that bridges TuneLab with DAWs, enabling real-time audio streaming from TuneLab tracks directly into your DAW.

## Features

- **Real-time Audio Streaming**: Receive audio from TuneLab tracks in real-time
- **Track Selection**: Choose which TuneLab track to stream
- **Transport Synchronization**: Bidirectional transport control between DAW and TuneLab
- **Low-latency IPC**: Lock-free shared memory ring buffer for audio streaming
- **Cross-platform**: Windows support (macOS/Linux planned)

## Architecture

```
┌─────────────────┐     Named Pipes (JSON)      ┌─────────────────┐
│   TuneLab       │ ◄──────────────────────────►│  VST3 Plugin    │
│   (C# Server)   │     Shared Memory (Audio)   │  (This Plugin)  │
│                 │ ────────────────────────────►│                 │
└─────────────────┘                             └─────────────────┘
```

- **Named Pipes**: Bidirectional control messages (connect, selectTrack, transport, seek)
- **Shared Memory**: Lock-free SPSC ring buffer for real-time audio (48kHz, stereo)

## Requirements

- **JUCE Framework 8.0.4+**: Download from [juce.com](https://juce.com/)
- **CMake 3.22+**
- **Visual Studio 2022** (Windows) or Xcode (macOS)
- **C++17 compatible compiler**

## Building

### Windows

```bash
# Clone with JUCE submodule
git clone --recursive https://github.com/YourUsername/TuneLabBridge.git
cd TuneLabBridge

# If you forgot --recursive
git submodule update --init --recursive

# Build with CMake
mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
```

### macOS

```bash
mkdir build && cd build
cmake .. -G Xcode
cmake --build . --config Release
```

## Installation

Copy the built VST3 plugin to your system's VST3 directory:

- **Windows**: `C:\Program Files\Common Files\VST3\`
- **macOS**: `/Library/Audio/Plug-Ins/VST3/`
- **Linux**: `~/.vst3/`

## Usage

1. Launch **TuneLab** (BridgeService starts automatically)
2. Open your **DAW** and add "TuneLab Bridge" as an instrument
3. Click **Connect** in the plugin UI
4. Select a **track** from the dropdown
5. Play in the DAW to receive audio from the selected TuneLab track

## Protocol

The plugin communicates with TuneLab using:

### Commands (Named Pipe → TuneLab)
| Command | Description |
|---------|-------------|
| `connect` | Establish connection, receive track list |
| `selectTrack` | Select which track to stream audio from |
| `transport` | Send play/pause/stop commands |
| `seek` | Seek to a specific position |

### Events (TuneLab → Named Pipe)
| Event | Description |
|-------|-------------|
| `trackListChanged` | Track list updated |
| `transportChanged` | Transport state changed |
| `positionChanged` | Playback position updated |

## License

MIT License - See [LICENSE](LICENSE) file

## Related Projects

- [TuneLab](https://github.com/LiuYunPlayer/TuneLab) - The main TuneLab application
- [JUCE](https://juce.com/) - Cross-platform C++ audio framework
