#pragma once

#include <cstdint>
#include <string>

namespace TuneLabBridge
{

/**
 * Protocol constants for TuneLab Bridge communication.
 * Must match the C# BridgeProtocol class.
 */
namespace Protocol
{
    // Named pipe name for control messages
    constexpr const char* PipeName = "TuneLabBridge";
    
    // Prefix for shared memory names. Client ID is appended.
    constexpr const char* ShmNamePrefix = "TuneLabAudio_";
    
    // Magic number to identify valid shared memory: "TLBG" (TuneLab Bridge)
    constexpr uint32_t MagicNumber = 0x544C4247;
    
    // Current protocol version
    constexpr uint32_t ProtocolVersion = 1;
    
    // Default buffer size in samples per channel (1 second at 48kHz)
    constexpr int DefaultBufferSamples = 48000;
    
    // Size of the shared memory header in bytes
    constexpr int HeaderSize = 64;
    
    // Maximum number of concurrent clients
    constexpr int MaxClients = 8;
}

/**
 * Shared memory header structure (64 bytes).
 * This structure is placed at the beginning of the shared memory region.
 * Must match the C# SharedMemoryHeader struct exactly.
 */
#pragma pack(push, 1)
struct SharedMemoryHeader
{
    uint32_t magic;           // Magic number: 0x544C4247 ("TLBG")
    uint32_t version;         // Protocol version number
    uint32_t sampleRate;      // Sample rate in Hz
    uint32_t bufferSize;      // Buffer size in samples per channel
    int64_t writePosition;    // Write position in samples (atomic)
    int64_t readPosition;     // Read position in samples (atomic)
    uint32_t statusFlags;     // Status flags
    uint32_t channelCount;    // Number of channels (1=mono, 2=stereo)
    int64_t playbackPosition; // Current playback position in samples
    uint8_t reserved[16];     // Reserved for future use
    
    bool isValid() const { return magic == Protocol::MagicNumber; }
    bool isConnected() const { return (statusFlags & 0x01) != 0; }
    bool isPlaying() const { return (statusFlags & 0x02) != 0; }
    bool hasError() const { return (statusFlags & 0x04) != 0; }
};
#pragma pack(pop)

static_assert(sizeof(SharedMemoryHeader) == 64, "SharedMemoryHeader must be exactly 64 bytes");

/**
 * Status flags for the shared memory header.
 */
enum class StatusFlags : uint32_t
{
    None = 0,
    Connected = 0x01,
    Playing = 0x02,
    Error = 0x04
};

/**
 * Command actions for the bridge protocol.
 */
namespace Actions
{
    constexpr const char* Connect = "connect";
    constexpr const char* Disconnect = "disconnect";
    constexpr const char* GetTrackList = "getTrackList";
    constexpr const char* SelectTrack = "selectTrack";
    constexpr const char* Transport = "transport";
    constexpr const char* Seek = "seek";
    constexpr const char* RequestAudio = "requestAudio";
}

/**
 * Event names for the bridge protocol.
 */
namespace Events
{
    constexpr const char* TrackListChanged = "trackListChanged";
    constexpr const char* TransportChanged = "transportChanged";
    constexpr const char* TrackChanged = "trackChanged";
    constexpr const char* ConnectionLost = "connectionLost";
}

} // namespace TuneLabBridge
