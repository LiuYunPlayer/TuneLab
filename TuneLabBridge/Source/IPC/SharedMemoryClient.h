#pragma once

#include "../Model/BridgeProtocol.h"
#include <string>
#include <atomic>

#ifdef _WIN32
    #include <windows.h>
#else
    #include <sys/mman.h>
    #include <fcntl.h>
    #include <unistd.h>
#endif

namespace TuneLabBridge
{

/**
 * Client-side shared memory reader for receiving audio data from TuneLab.
 */
class SharedMemoryClient
{
public:
    SharedMemoryClient();
    ~SharedMemoryClient();
    
    // Non-copyable
    SharedMemoryClient(const SharedMemoryClient&) = delete;
    SharedMemoryClient& operator=(const SharedMemoryClient&) = delete;
    
    /**
     * Opens an existing shared memory region.
     * @param clientId The client ID (used to construct the shared memory name)
     * @return True if successful
     */
    bool open(const std::string& clientId);
    
    /**
     * Closes the shared memory.
     */
    void close();
    
    /**
     * Checks if the shared memory is open.
     */
    bool isOpen() const { return m_isOpen; }
    
    /**
     * Checks if connected to TuneLab.
     */
    bool isConnected() const;
    
    /**
     * Reads the shared memory header.
     */
    SharedMemoryHeader readHeader() const;
    
    /**
     * Gets the current write position.
     */
    int64_t getWritePosition() const;
    
    /**
     * Gets the current read position.
     */
    int64_t getReadPosition() const;
    
    /**
     * Updates the read position (atomic).
     */
    void updateReadPosition(int64_t position);
    
    /**
     * Reads audio samples from the left channel buffer.
     * @param dest Destination buffer
     * @param offset Offset in samples from start of buffer
     * @param count Number of samples to read
     */
    void readLeftChannel(float* dest, size_t offset, size_t count);
    
    /**
     * Reads audio samples from the right channel buffer.
     * @param dest Destination buffer
     * @param offset Offset in samples from start of buffer
     * @param count Number of samples to read
     */
    void readRightChannel(float* dest, size_t offset, size_t count);
    
    /**
     * Reads stereo samples from the ring buffer.
     * Handles wrap-around automatically.
     * @param leftDest Left channel destination
     * @param rightDest Right channel destination
     * @param readPos Current read position
     * @param count Number of samples to read
     * @return Number of samples actually read
     */
    size_t readStereoSamples(float* leftDest, float* rightDest, int64_t& readPos, size_t count);
    
    /**
     * Gets the number of samples available for reading.
     */
    size_t getAvailableSamples() const;
    
    /**
     * Gets the buffer size in samples per channel.
     */
    size_t getBufferSize() const;
    
    /**
     * Gets the sample rate.
     */
    int getSampleRate() const;
    
private:
    std::string m_name;
    bool m_isOpen = false;
    
#ifdef _WIN32
    HANDLE m_hMapFile = nullptr;
#else
    int m_fd = -1;
#endif
    
    void* m_mappedMemory = nullptr;
    size_t m_mappedSize = 0;
    
    // Cached values from header
    size_t m_bufferSamples = 0;
    size_t m_channelCount = 2;
    
    // Pointers to buffer regions
    float* m_leftBuffer = nullptr;
    float* m_rightBuffer = nullptr;
};

} // namespace TuneLabBridge
