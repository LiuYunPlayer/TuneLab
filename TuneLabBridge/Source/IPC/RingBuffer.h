#pragma once

#include <atomic>
#include <vector>
#include <cstring>
#include <algorithm>

namespace TuneLabBridge
{

/**
 * Lock-free Single Producer Single Consumer (SPSC) ring buffer.
 * Designed for real-time audio data transfer between the shared memory
 * reader thread and the audio processing thread.
 */
template<typename T>
class RingBuffer
{
public:
    explicit RingBuffer(size_t capacity)
        : m_buffer(capacity)
        , m_capacity(capacity)
        , m_readPos(0)
        , m_writePos(0)
    {
    }
    
    /**
     * Returns the capacity of the buffer.
     */
    size_t getCapacity() const { return m_capacity; }
    
    /**
     * Returns the number of samples available for reading.
     */
    size_t getAvailableForReading() const
    {
        const size_t write = m_writePos.load(std::memory_order_acquire);
        const size_t read = m_readPos.load(std::memory_order_relaxed);
        return write - read;
    }
    
    /**
     * Returns the number of samples that can be written.
     */
    size_t getAvailableForWriting() const
    {
        return m_capacity - getAvailableForReading();
    }
    
    /**
     * Reads samples from the buffer.
     * @param dest Destination array
     * @param count Number of samples to read
     * @return Number of samples actually read
     */
    size_t read(T* dest, size_t count)
    {
        const size_t available = getAvailableForReading();
        const size_t toRead = std::min(count, available);
        
        if (toRead == 0)
            return 0;
        
        const size_t readPos = m_readPos.load(std::memory_order_relaxed);
        const size_t bufferPos = readPos % m_capacity;
        
        // Handle wrap-around
        const size_t firstPart = std::min(toRead, m_capacity - bufferPos);
        const size_t secondPart = toRead - firstPart;
        
        std::memcpy(dest, m_buffer.data() + bufferPos, firstPart * sizeof(T));
        if (secondPart > 0)
        {
            std::memcpy(dest + firstPart, m_buffer.data(), secondPart * sizeof(T));
        }
        
        m_readPos.store(readPos + toRead, std::memory_order_release);
        return toRead;
    }
    
    /**
     * Writes samples to the buffer.
     * @param src Source array
     * @param count Number of samples to write
     * @return Number of samples actually written
     */
    size_t write(const T* src, size_t count)
    {
        const size_t available = getAvailableForWriting();
        const size_t toWrite = std::min(count, available);
        
        if (toWrite == 0)
            return 0;
        
        const size_t writePos = m_writePos.load(std::memory_order_relaxed);
        const size_t bufferPos = writePos % m_capacity;
        
        // Handle wrap-around
        const size_t firstPart = std::min(toWrite, m_capacity - bufferPos);
        const size_t secondPart = toWrite - firstPart;
        
        std::memcpy(m_buffer.data() + bufferPos, src, firstPart * sizeof(T));
        if (secondPart > 0)
        {
            std::memcpy(m_buffer.data(), src + firstPart, secondPart * sizeof(T));
        }
        
        m_writePos.store(writePos + toWrite, std::memory_order_release);
        return toWrite;
    }
    
    /**
     * Clears the buffer by resetting read/write positions.
     */
    void reset()
    {
        m_readPos.store(0, std::memory_order_relaxed);
        m_writePos.store(0, std::memory_order_relaxed);
    }
    
    /**
     * Checks if the buffer is empty.
     */
    bool isEmpty() const
    {
        return getAvailableForReading() == 0;
    }
    
private:
    std::vector<T> m_buffer;
    const size_t m_capacity;
    std::atomic<size_t> m_readPos;
    std::atomic<size_t> m_writePos;
};

/**
 * Stereo ring buffer that manages L/R channels together.
 */
class StereoRingBuffer
{
public:
    explicit StereoRingBuffer(size_t samplesPerChannel)
        : m_left(samplesPerChannel)
        , m_right(samplesPerChannel)
    {
    }
    
    size_t getCapacity() const { return m_left.getCapacity(); }
    size_t getAvailableForReading() const { return m_left.getAvailableForReading(); }
    size_t getAvailableForWriting() const { return m_left.getAvailableForWriting(); }
    
    /**
     * Reads interleaved stereo samples.
     */
    size_t readInterleaved(float* dest, size_t frames)
    {
        const size_t available = getAvailableForReading();
        const size_t toRead = std::min(frames, available);
        
        if (toRead == 0)
            return 0;
        
        // Temporary buffers
        std::vector<float> leftBuf(toRead);
        std::vector<float> rightBuf(toRead);
        
        m_left.read(leftBuf.data(), toRead);
        m_right.read(rightBuf.data(), toRead);
        
        // Interleave
        for (size_t i = 0; i < toRead; ++i)
        {
            dest[i * 2] = leftBuf[i];
            dest[i * 2 + 1] = rightBuf[i];
        }
        
        return toRead;
    }
    
    /**
     * Reads separate channel samples.
     */
    size_t readSeparate(float* left, float* right, size_t frames)
    {
        const size_t readLeft = m_left.read(left, frames);
        const size_t readRight = m_right.read(right, frames);
        return std::min(readLeft, readRight);
    }
    
    /**
     * Writes separate channel samples.
     */
    size_t writeSeparate(const float* left, const float* right, size_t frames)
    {
        const size_t writeLeft = m_left.write(left, frames);
        const size_t writeRight = m_right.write(right, frames);
        return std::min(writeLeft, writeRight);
    }
    
    /**
     * Writes interleaved stereo samples.
     */
    size_t writeInterleaved(const float* src, size_t frames)
    {
        const size_t available = getAvailableForWriting();
        const size_t toWrite = std::min(frames, available);
        
        if (toWrite == 0)
            return 0;
        
        // De-interleave
        std::vector<float> leftBuf(toWrite);
        std::vector<float> rightBuf(toWrite);
        
        for (size_t i = 0; i < toWrite; ++i)
        {
            leftBuf[i] = src[i * 2];
            rightBuf[i] = src[i * 2 + 1];
        }
        
        m_left.write(leftBuf.data(), toWrite);
        m_right.write(rightBuf.data(), toWrite);
        
        return toWrite;
    }
    
    void reset()
    {
        m_left.reset();
        m_right.reset();
    }
    
private:
    RingBuffer<float> m_left;
    RingBuffer<float> m_right;
};

} // namespace TuneLabBridge
