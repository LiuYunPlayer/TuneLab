#include "SharedMemoryClient.h"
#include <cstring>
#include <algorithm>
#include <atomic>

#ifdef _WIN32
#include <intrin.h>
#pragma intrinsic(_InterlockedExchange64)
#pragma intrinsic(_InterlockedCompareExchange64)
#endif

namespace TuneLabBridge
{

// Cross-platform atomic read for int64_t
inline int64_t atomicLoad64(volatile int64_t* ptr)
{
#ifdef _WIN32
    return _InterlockedCompareExchange64(ptr, 0, 0);
#else
    return __atomic_load_n(ptr, __ATOMIC_ACQUIRE);
#endif
}

// Cross-platform atomic write for int64_t
inline void atomicStore64(volatile int64_t* ptr, int64_t value)
{
#ifdef _WIN32
    _InterlockedExchange64(ptr, value);
#else
    __atomic_store_n(ptr, value, __ATOMIC_RELEASE);
#endif
}

SharedMemoryClient::SharedMemoryClient()
{
}

SharedMemoryClient::~SharedMemoryClient()
{
    close();
}

bool SharedMemoryClient::open(const std::string& clientId)
{
    if (m_isOpen)
        close();
    
    m_name = std::string(Protocol::ShmNamePrefix) + clientId;
    
#ifdef _WIN32
    // Windows: Open existing memory mapped file
    m_hMapFile = OpenFileMappingA(
        FILE_MAP_READ,
        FALSE,
        m_name.c_str()
    );
    
    if (m_hMapFile == nullptr)
        return false;
    
    // First map just the header to get buffer size
    void* headerPtr = MapViewOfFile(
        m_hMapFile,
        FILE_MAP_READ,
        0, 0, Protocol::HeaderSize
    );
    
    if (headerPtr == nullptr)
    {
        CloseHandle(m_hMapFile);
        m_hMapFile = nullptr;
        return false;
    }
    
    auto header = *reinterpret_cast<SharedMemoryHeader*>(headerPtr);
    UnmapViewOfFile(headerPtr);
    
    if (!header.isValid())
    {
        CloseHandle(m_hMapFile);
        m_hMapFile = nullptr;
        return false;
    }
    
    m_bufferSamples = header.bufferSize;
    m_channelCount = header.channelCount;
    
    // Calculate total size and remap
    m_mappedSize = Protocol::HeaderSize + (m_bufferSamples * sizeof(float) * m_channelCount);
    m_mappedMemory = MapViewOfFile(
        m_hMapFile,
        FILE_MAP_READ | FILE_MAP_WRITE, // Need write for read position update
        0, 0, m_mappedSize
    );
    
    if (m_mappedMemory == nullptr)
    {
        CloseHandle(m_hMapFile);
        m_hMapFile = nullptr;
        return false;
    }
    
#else
    // POSIX: Open existing shared memory
    m_fd = shm_open(m_name.c_str(), O_RDWR, 0666);
    if (m_fd < 0)
        return false;
    
    // First map just the header
    void* headerPtr = mmap(nullptr, Protocol::HeaderSize, PROT_READ, MAP_SHARED, m_fd, 0);
    if (headerPtr == MAP_FAILED)
    {
        ::close(m_fd);
        m_fd = -1;
        return false;
    }
    
    auto header = *reinterpret_cast<SharedMemoryHeader*>(headerPtr);
    munmap(headerPtr, Protocol::HeaderSize);
    
    if (!header.isValid())
    {
        ::close(m_fd);
        m_fd = -1;
        return false;
    }
    
    m_bufferSamples = header.bufferSize;
    m_channelCount = header.channelCount;
    
    // Calculate total size and remap
    m_mappedSize = Protocol::HeaderSize + (m_bufferSamples * sizeof(float) * m_channelCount);
    m_mappedMemory = mmap(nullptr, m_mappedSize, PROT_READ | PROT_WRITE, MAP_SHARED, m_fd, 0);
    
    if (m_mappedMemory == MAP_FAILED)
    {
        ::close(m_fd);
        m_fd = -1;
        m_mappedMemory = nullptr;
        return false;
    }
#endif
    
    // Set up buffer pointers
    auto basePtr = reinterpret_cast<uint8_t*>(m_mappedMemory);
    m_leftBuffer = reinterpret_cast<float*>(basePtr + Protocol::HeaderSize);
    m_rightBuffer = reinterpret_cast<float*>(basePtr + Protocol::HeaderSize + m_bufferSamples * sizeof(float));
    
    m_isOpen = true;
    return true;
}

void SharedMemoryClient::close()
{
    if (!m_isOpen)
        return;
    
#ifdef _WIN32
    if (m_mappedMemory != nullptr)
    {
        UnmapViewOfFile(m_mappedMemory);
        m_mappedMemory = nullptr;
    }
    if (m_hMapFile != nullptr)
    {
        CloseHandle(m_hMapFile);
        m_hMapFile = nullptr;
    }
#else
    if (m_mappedMemory != nullptr)
    {
        munmap(m_mappedMemory, m_mappedSize);
        m_mappedMemory = nullptr;
    }
    if (m_fd >= 0)
    {
        ::close(m_fd);
        m_fd = -1;
    }
#endif
    
    m_leftBuffer = nullptr;
    m_rightBuffer = nullptr;
    m_isOpen = false;
}

bool SharedMemoryClient::isConnected() const
{
    if (!m_isOpen)
        return false;
    
    return readHeader().isConnected();
}

SharedMemoryHeader SharedMemoryClient::readHeader() const
{
    if (!m_isOpen || m_mappedMemory == nullptr)
        return SharedMemoryHeader{};
    
    return *reinterpret_cast<SharedMemoryHeader*>(m_mappedMemory);
}

int64_t SharedMemoryClient::getWritePosition() const
{
    if (!m_isOpen)
        return 0;
    
    auto* header = reinterpret_cast<SharedMemoryHeader*>(m_mappedMemory);
    return atomicLoad64(&header->writePosition);
}

int64_t SharedMemoryClient::getReadPosition() const
{
    if (!m_isOpen)
        return 0;
    
    auto* header = reinterpret_cast<SharedMemoryHeader*>(m_mappedMemory);
    return atomicLoad64(&header->readPosition);
}

void SharedMemoryClient::updateReadPosition(int64_t position)
{
    if (!m_isOpen)
        return;
    
    auto* header = reinterpret_cast<SharedMemoryHeader*>(m_mappedMemory);
    atomicStore64(&header->readPosition, position);
}

void SharedMemoryClient::readLeftChannel(float* dest, size_t offset, size_t count)
{
    if (!m_isOpen || m_leftBuffer == nullptr)
        return;
    
    std::memcpy(dest, m_leftBuffer + offset, count * sizeof(float));
}

void SharedMemoryClient::readRightChannel(float* dest, size_t offset, size_t count)
{
    if (!m_isOpen || m_rightBuffer == nullptr)
        return;
    
    std::memcpy(dest, m_rightBuffer + offset, count * sizeof(float));
}

size_t SharedMemoryClient::readStereoSamples(float* leftDest, float* rightDest, int64_t& readPos, size_t count)
{
    if (!m_isOpen)
        return 0;
    
    const int64_t writePos = getWritePosition();
    const int64_t available = writePos - readPos;
    
    if (available <= 0)
        return 0;
    
    const size_t toRead = (std::min)(count, static_cast<size_t>(available));
    
    // Handle wrap-around
    const size_t bufferPos = static_cast<size_t>(readPos % m_bufferSamples);
    const size_t firstPart = (std::min)(toRead, m_bufferSamples - bufferPos);
    const size_t secondPart = toRead - firstPart;
    
    // Read first part
    readLeftChannel(leftDest, bufferPos, firstPart);
    readRightChannel(rightDest, bufferPos, firstPart);
    
    // Read second part if wrapped
    if (secondPart > 0)
    {
        readLeftChannel(leftDest + firstPart, 0, secondPart);
        readRightChannel(rightDest + firstPart, 0, secondPart);
    }
    
    // Update read position
    readPos += toRead;
    updateReadPosition(readPos);
    
    return toRead;
}

size_t SharedMemoryClient::getAvailableSamples() const
{
    if (!m_isOpen)
        return 0;
    
    const int64_t writePos = getWritePosition();
    const int64_t readPos = getReadPosition();
    
    if (writePos <= readPos)
        return 0;
    
    return static_cast<size_t>(writePos - readPos);
}

size_t SharedMemoryClient::getBufferSize() const
{
    return m_bufferSamples;
}

int SharedMemoryClient::getSampleRate() const
{
    if (!m_isOpen)
        return 0;
    
    return static_cast<int>(readHeader().sampleRate);
}

} // namespace TuneLabBridge
