using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Bridge;

/// <summary>
/// Represents a connected VST3 plugin client.
/// Manages the shared memory for audio streaming to this specific client.
/// </summary>
public class BridgeClient : IDisposable
{
    private SharedMemoryServer? _sharedMemory;
    private long _writePosition;
    private readonly object _audioLock = new();
    private bool _disposed;
    
    /// <summary>
    /// Unique client identifier.
    /// </summary>
    public string ClientId { get; }
    
    /// <summary>
    /// Sample rate requested by the client.
    /// </summary>
    public int SampleRate { get; private set; }
    
    /// <summary>
    /// Buffer size requested by the client.
    /// </summary>
    public int BufferSize { get; private set; }
    
    /// <summary>
    /// Currently selected track ID.
    /// </summary>
    public string? SelectedTrackId { get; set; }
    
    /// <summary>
    /// Current transport state.
    /// </summary>
    public TransportState TransportState { get; } = new();
    
    /// <summary>
    /// Whether the client is currently connected and active.
    /// </summary>
    public bool IsConnected => _sharedMemory?.IsOpen ?? false;
    
    /// <summary>
    /// Event raised when the selected track changes.
    /// </summary>
    public event Action<BridgeClient, string?>? TrackChanged;
    
    /// <summary>
    /// Event raised when transport state changes.
    /// </summary>
    public event Action<BridgeClient, TransportState>? TransportChanged;
    
    public BridgeClient(string clientId)
    {
        ClientId = clientId;
    }
    
    /// <summary>
    /// Initializes the client with connection parameters.
    /// Creates the shared memory for audio streaming.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz</param>
    /// <param name="bufferSize">Buffer size in samples</param>
    /// <returns>True if initialization successful</returns>
    public bool Initialize(int sampleRate, int bufferSize)
    {
        SampleRate = sampleRate;
        BufferSize = bufferSize;
        TransportState.SampleRate = sampleRate;
        
        // Create shared memory for audio - 1 second buffer
        _sharedMemory = new SharedMemoryServer(ClientId, BridgeProtocol.DefaultBufferSamples, 2);
        
        if (!_sharedMemory.Create(sampleRate))
        {
            Log.Error($"BridgeClient: Failed to create shared memory for {ClientId}");
            return false;
        }
        
        _writePosition = 0;
        Log.Info($"BridgeClient: Initialized client {ClientId} (SR: {sampleRate}, Buffer: {bufferSize})");
        return true;
    }
    
    /// <summary>
    /// Selects a track for audio streaming.
    /// </summary>
    /// <param name="trackId">Track ID to select, or null for master</param>
    public void SelectTrack(string? trackId)
    {
        if (SelectedTrackId != trackId)
        {
            SelectedTrackId = trackId;
            
            // Reset write position when changing tracks
            lock (_audioLock)
            {
                _writePosition = 0;
                if (_sharedMemory != null)
                {
                    var header = _sharedMemory.ReadHeader();
                    header.WritePosition = 0;
                    header.ReadPosition = 0;
                    _sharedMemory.WriteHeader(header);
                }
            }
            
            TrackChanged?.Invoke(this, trackId);
            Log.Info($"BridgeClient: {ClientId} selected track: {trackId ?? "master"}");
        }
    }
    
    /// <summary>
    /// Updates the transport state from plugin.
    /// </summary>
    /// <param name="isPlaying">Whether playback is active</param>
    /// <param name="position">Position in seconds</param>
    public void UpdateTransport(bool isPlaying, double position)
    {
        bool changed = TransportState.IsPlaying != isPlaying || 
                       Math.Abs(TransportState.Position - position) > 0.001;
        
        TransportState.IsPlaying = isPlaying;
        TransportState.Position = position;
        
        if (changed)
        {
            // Update shared memory status
            if (_sharedMemory != null)
            {
                var flags = BridgeStatusFlags.Connected;
                if (isPlaying) flags |= BridgeStatusFlags.Playing;
                _sharedMemory.SetStatusFlags(flags);
                _sharedMemory.SetPlaybackPosition((long)(position * SampleRate));
            }
            
            TransportChanged?.Invoke(this, TransportState);
        }
    }
    
    /// <summary>
    /// Seeks to a specific position.
    /// </summary>
    /// <param name="position">Position in seconds</param>
    public void Seek(double position)
    {
        TransportState.Position = position;
        
        // Reset buffer on seek
        lock (_audioLock)
        {
            _writePosition = 0;
            if (_sharedMemory != null)
            {
                var header = _sharedMemory.ReadHeader();
                header.WritePosition = 0;
                header.ReadPosition = 0;
                header.PlaybackPosition = (long)(position * SampleRate);
                _sharedMemory.WriteHeader(header);
            }
        }
        
        TransportChanged?.Invoke(this, TransportState);
        Log.Info($"BridgeClient: {ClientId} seeked to {position:F3}s");
    }
    
    /// <summary>
    /// Writes audio data to the shared memory ring buffer.
    /// </summary>
    /// <param name="leftChannel">Left channel samples</param>
    /// <param name="rightChannel">Right channel samples</param>
    /// <param name="sampleCount">Number of samples</param>
    /// <returns>Number of samples actually written</returns>
    public int WriteAudio(float[] leftChannel, float[] rightChannel, int sampleCount)
    {
        if (_sharedMemory == null || !_sharedMemory.IsOpen)
            return 0;
        
        lock (_audioLock)
        {
            return _sharedMemory.WriteAudio(leftChannel, rightChannel, sampleCount, ref _writePosition);
        }
    }
    
    /// <summary>
    /// Writes interleaved stereo audio data to the shared memory.
    /// </summary>
    /// <param name="interleavedSamples">Interleaved L/R samples</param>
    /// <param name="sampleCount">Number of sample frames</param>
    /// <returns>Number of sample frames actually written</returns>
    public int WriteInterleavedAudio(float[] interleavedSamples, int sampleCount)
    {
        if (_sharedMemory == null || !_sharedMemory.IsOpen)
            return 0;
        
        lock (_audioLock)
        {
            return _sharedMemory.WriteInterleavedAudio(interleavedSamples, sampleCount, ref _writePosition);
        }
    }
    
    /// <summary>
    /// Gets available space in the ring buffer.
    /// </summary>
    /// <returns>Number of samples that can be written</returns>
    public int GetAvailableSpace()
    {
        if (_sharedMemory == null) return 0;
        
        var readPos = _sharedMemory.GetReadPosition();
        return (int)(BridgeProtocol.DefaultBufferSamples - (_writePosition - readPos));
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _sharedMemory?.Dispose();
        _sharedMemory = null;
        
        Log.Info($"BridgeClient: Disposed client {ClientId}");
    }
}
