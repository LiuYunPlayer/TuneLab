using System;
using System.Runtime.InteropServices;

namespace TuneLab.Bridge;

/// <summary>
/// Protocol constants and shared memory header structure for TuneLab Bridge communication.
/// </summary>
public static class BridgeProtocol
{
    /// <summary>
    /// Named pipe name for control messages.
    /// </summary>
    public const string PipeName = "TuneLabBridge";
    
    /// <summary>
    /// Prefix for shared memory names. Client ID is appended.
    /// </summary>
    public const string ShmNamePrefix = "TuneLabAudio_";
    
    /// <summary>
    /// Magic number to identify valid shared memory: "TLBG" (TuneLab Bridge)
    /// </summary>
    public const uint MagicNumber = 0x544C4247;
    
    /// <summary>
    /// Current protocol version.
    /// </summary>
    public const uint ProtocolVersion = 1;
    
    /// <summary>
    /// Default buffer size in samples per channel (1 second at 48kHz).
    /// </summary>
    public const int DefaultBufferSamples = 48000;
    
    /// <summary>
    /// Size of the shared memory header in bytes.
    /// </summary>
    public const int HeaderSize = 64;
    
    /// <summary>
    /// Maximum number of concurrent clients.
    /// </summary>
    public const int MaxClients = 8;
}

/// <summary>
/// Shared memory header structure (64 bytes).
/// This structure is placed at the beginning of the shared memory region.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SharedMemoryHeader
{
    /// <summary>
    /// Magic number: 0x544C4247 ("TLBG")
    /// </summary>
    public uint Magic;
    
    /// <summary>
    /// Protocol version number.
    /// </summary>
    public uint Version;
    
    /// <summary>
    /// Sample rate in Hz.
    /// </summary>
    public uint SampleRate;
    
    /// <summary>
    /// Buffer size in samples per channel.
    /// </summary>
    public uint BufferSize;
    
    /// <summary>
    /// Write position in samples (atomic).
    /// </summary>
    public long WritePosition;
    
    /// <summary>
    /// Read position in samples (atomic).
    /// </summary>
    public long ReadPosition;
    
    /// <summary>
    /// Status flags.
    /// Bit 0: Connected
    /// Bit 1: Playing
    /// Bit 2: Error
    /// </summary>
    public uint StatusFlags;
    
    /// <summary>
    /// Number of channels (1=mono, 2=stereo).
    /// </summary>
    public uint ChannelCount;
    
    /// <summary>
    /// Current playback position in samples from start.
    /// </summary>
    public long PlaybackPosition;
    
    /// <summary>
    /// Reserved for future use to pad to 64 bytes.
    /// </summary>
    public unsafe fixed byte Reserved[16];
    
    public bool IsValid => Magic == BridgeProtocol.MagicNumber;
    public bool IsConnected => (StatusFlags & 0x01) != 0;
    public bool IsPlaying => (StatusFlags & 0x02) != 0;
    public bool HasError => (StatusFlags & 0x04) != 0;
}

/// <summary>
/// Status flags for the shared memory header.
/// </summary>
[Flags]
public enum BridgeStatusFlags : uint
{
    None = 0,
    Connected = 0x01,
    Playing = 0x02,
    Error = 0x04,
}

/// <summary>
/// Track information for the track list.
/// </summary>
public class TrackInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "midi"; // "midi" or "audio"
    public bool IsMute { get; set; }
    public bool IsSolo { get; set; }
    public double Volume { get; set; } = 1.0;
    public double Pan { get; set; } = 0.0;
    public double Duration { get; set; }
}

/// <summary>
/// Transport state for synchronization.
/// </summary>
public class TransportState
{
    public bool IsPlaying { get; set; }
    public double Position { get; set; }
    public double SampleRate { get; set; }
    public double Tempo { get; set; } = 120.0;
}
