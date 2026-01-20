using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using TuneLab.Base.Utils;

namespace TuneLab.Bridge;

/// <summary>
/// Server-side shared memory for audio buffer communication.
/// Implements a lock-free SPSC (Single Producer Single Consumer) ring buffer.
/// </summary>
public class SharedMemoryServer : IDisposable
{
    private readonly string _name;
    private readonly int _bufferSamples;
    private readonly int _channelCount;
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private IntPtr _basePointer;
    private bool _disposed;
    
    /// <summary>
    /// Total size of the shared memory region in bytes.
    /// </summary>
    public int TotalSize { get; }
    
    /// <summary>
    /// Size of the audio buffer region in bytes (per channel).
    /// </summary>
    public int BufferSizeBytes => _bufferSamples * sizeof(float);
    
    /// <summary>
    /// Whether the shared memory is currently open.
    /// </summary>
    public bool IsOpen => _mmf != null;
    
    /// <summary>
    /// Creates a new SharedMemoryServer.
    /// </summary>
    /// <param name="clientId">Unique client identifier</param>
    /// <param name="bufferSamples">Number of samples per channel</param>
    /// <param name="channelCount">Number of audio channels (1 or 2)</param>
    public SharedMemoryServer(string clientId, int bufferSamples = 48000, int channelCount = 2)
    {
        _name = BridgeProtocol.ShmNamePrefix + clientId;
        _bufferSamples = bufferSamples;
        _channelCount = channelCount;
        
        // Calculate total size: header + (samples * sizeof(float) * channels)
        TotalSize = BridgeProtocol.HeaderSize + (bufferSamples * sizeof(float) * channelCount);
    }
    
    /// <summary>
    /// Creates and initializes the shared memory region.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz</param>
    /// <returns>True if successful</returns>
    public bool Create(int sampleRate)
    {
        try
        {
            // Create the memory mapped file
            _mmf = MemoryMappedFile.CreateOrOpen(_name, TotalSize, MemoryMappedFileAccess.ReadWrite);
            _accessor = _mmf.CreateViewAccessor(0, TotalSize, MemoryMappedFileAccess.ReadWrite);
            
            // Get the base pointer for direct memory access
            unsafe
            {
                byte* ptr = null;
                _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                _basePointer = (IntPtr)ptr;
            }
            
            // Initialize the header
            InitializeHeader(sampleRate);
            
            Log.Info($"SharedMemoryServer: Created shared memory '{_name}' ({TotalSize} bytes)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"SharedMemoryServer: Failed to create shared memory: {ex.Message}");
            Dispose();
            return false;
        }
    }
    
    private void InitializeHeader(int sampleRate)
    {
        if (_accessor == null) return;
        
        var header = new SharedMemoryHeader
        {
            Magic = BridgeProtocol.MagicNumber,
            Version = BridgeProtocol.ProtocolVersion,
            SampleRate = (uint)sampleRate,
            BufferSize = (uint)_bufferSamples,
            WritePosition = 0,
            ReadPosition = 0,
            StatusFlags = (uint)BridgeStatusFlags.Connected,
            ChannelCount = (uint)_channelCount,
            PlaybackPosition = 0
        };
        
        WriteHeader(header);
    }
    
    /// <summary>
    /// Writes the header structure to shared memory.
    /// </summary>
    public void WriteHeader(SharedMemoryHeader header)
    {
        if (_accessor == null) return;
        _accessor.Write(0, ref header);
    }
    
    /// <summary>
    /// Reads the header structure from shared memory.
    /// </summary>
    public SharedMemoryHeader ReadHeader()
    {
        if (_accessor == null) return default;
        _accessor.Read(0, out SharedMemoryHeader header);
        return header;
    }
    
    /// <summary>
    /// Updates the write position atomically.
    /// </summary>
    public unsafe void UpdateWritePosition(long position)
    {
        if (_accessor == null) return;
        // Offset of WritePosition in header: 4 + 4 + 4 + 4 = 16 bytes
        Interlocked.Exchange(ref System.Runtime.CompilerServices.Unsafe.AsRef<long>((void*)(_basePointer + 16)), position);
    }
    
    /// <summary>
    /// Reads the read position atomically.
    /// </summary>
    public unsafe long GetReadPosition()
    {
        if (_accessor == null) return 0;
        // Offset of ReadPosition in header: 16 + 8 = 24 bytes
        return Interlocked.Read(ref System.Runtime.CompilerServices.Unsafe.AsRef<long>((void*)(_basePointer + 24)));
    }
    
    /// <summary>
    /// Updates status flags.
    /// </summary>
    public void SetStatusFlags(BridgeStatusFlags flags)
    {
        if (_accessor == null) return;
        var header = ReadHeader();
        header.StatusFlags = (uint)flags;
        _accessor.Write(32, header.StatusFlags); // Offset of StatusFlags
    }
    
    /// <summary>
    /// Updates the playback position.
    /// </summary>
    public unsafe void SetPlaybackPosition(long position)
    {
        if (_accessor == null) return;
        // Offset of PlaybackPosition: 32 + 4 + 4 = 40 bytes
        Interlocked.Exchange(ref System.Runtime.CompilerServices.Unsafe.AsRef<long>((void*)(_basePointer + 40)), position);
    }
    
    /// <summary>
    /// Writes audio data to the ring buffer.
    /// </summary>
    /// <param name="leftChannel">Left channel samples</param>
    /// <param name="rightChannel">Right channel samples (can be null for mono)</param>
    /// <param name="sampleCount">Number of samples to write</param>
    /// <param name="writePos">Current write position (will be updated)</param>
    /// <returns>Number of samples actually written</returns>
    public int WriteAudio(float[] leftChannel, float[]? rightChannel, int sampleCount, ref long writePos)
    {
        if (_accessor == null || sampleCount <= 0) return 0;
        
        var readPos = GetReadPosition();
        
        // Calculate available space in the ring buffer
        long availableSpace = _bufferSamples - (writePos - readPos);
        if (availableSpace <= 0) return 0; // Buffer is full
        
        int samplesToWrite = (int)Math.Min(sampleCount, availableSpace);
        
        // Calculate buffer position with wrap-around
        int bufferPos = (int)(writePos % _bufferSamples);
        int leftOffset = BridgeProtocol.HeaderSize + bufferPos * sizeof(float);
        int rightOffset = BridgeProtocol.HeaderSize + _bufferSamples * sizeof(float) + bufferPos * sizeof(float);
        
        // Handle wrap-around case
        int firstPart = Math.Min(samplesToWrite, _bufferSamples - bufferPos);
        int secondPart = samplesToWrite - firstPart;
        
        // Write first part
        _accessor.WriteArray(leftOffset, leftChannel, 0, firstPart);
        if (_channelCount == 2 && rightChannel != null)
        {
            _accessor.WriteArray(rightOffset, rightChannel, 0, firstPart);
        }
        
        // Write second part if wrapped
        if (secondPart > 0)
        {
            int wrapLeftOffset = BridgeProtocol.HeaderSize;
            int wrapRightOffset = BridgeProtocol.HeaderSize + _bufferSamples * sizeof(float);
            
            _accessor.WriteArray(wrapLeftOffset, leftChannel, firstPart, secondPart);
            if (_channelCount == 2 && rightChannel != null)
            {
                _accessor.WriteArray(wrapRightOffset, rightChannel, firstPart, secondPart);
            }
        }
        
        // Update write position atomically
        writePos += samplesToWrite;
        UpdateWritePosition(writePos);
        
        return samplesToWrite;
    }
    
    /// <summary>
    /// Writes interleaved stereo audio data to the ring buffer.
    /// </summary>
    /// <param name="interleavedSamples">Interleaved L/R samples</param>
    /// <param name="sampleCount">Number of sample frames (pairs)</param>
    /// <param name="writePos">Current write position (will be updated)</param>
    /// <returns>Number of sample frames actually written</returns>
    public int WriteInterleavedAudio(float[] interleavedSamples, int sampleCount, ref long writePos)
    {
        if (_channelCount != 2 || sampleCount <= 0) return 0;
        
        // De-interleave into separate channels
        var left = new float[sampleCount];
        var right = new float[sampleCount];
        
        for (int i = 0; i < sampleCount; i++)
        {
            left[i] = interleavedSamples[i * 2];
            right[i] = interleavedSamples[i * 2 + 1];
        }
        
        return WriteAudio(left, right, sampleCount, ref writePos);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (_accessor != null)
        {
            // Set disconnected status before closing
            try
            {
                SetStatusFlags(BridgeStatusFlags.None);
            }
            catch { }
            
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _accessor.Dispose();
            _accessor = null;
        }
        
        _mmf?.Dispose();
        _mmf = null;
        
        Log.Info($"SharedMemoryServer: Disposed shared memory '{_name}'");
    }
}
