#pragma once

// Windows headers must come first
#ifdef _WIN32
    #ifndef NOMINMAX
        #define NOMINMAX
    #endif
    #include <windows.h>
#endif

#include <JuceHeader.h>
#include <fstream>
#include <mutex>
#include <chrono>
#include <iomanip>
#include <sstream>

namespace TuneLabBridge
{

/**
 * Simple file logger for debugging the VST plugin.
 * Logs are written to: %TEMP%/TuneLabBridge.log
 */
class Logger
{
public:
    static Logger& getInstance()
    {
        static Logger instance;
        return instance;
    }
    
    void log(const std::string& level, const std::string& message)
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        
        if (!m_file.is_open())
            return;
        
        auto now = std::chrono::system_clock::now();
        auto time = std::chrono::system_clock::to_time_t(now);
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
            now.time_since_epoch()) % 1000;
        
        std::tm tm;
#ifdef _WIN32
        localtime_s(&tm, &time);
#else
        localtime_r(&time, &tm);
#endif
        
        m_file << std::put_time(&tm, "%Y-%m-%d %H:%M:%S")
               << "." << std::setfill('0') << std::setw(3) << ms.count()
               << " [" << level << "] " << message << std::endl;
        m_file.flush();
    }
    
    void info(const std::string& message)
    {
        log("INFO", message);
    }
    
    void error(const std::string& message)
    {
        log("ERROR", message);
    }
    
    void debug(const std::string& message)
    {
        log("DEBUG", message);
    }
    
    std::string getLogFilePath() const
    {
        return m_logPath;
    }
    
private:
    Logger()
    {
        // Get temp directory and create log file
#ifdef _WIN32
        char tempPath[MAX_PATH];
        GetTempPathA(MAX_PATH, tempPath);
        m_logPath = std::string(tempPath) + "TuneLabBridge.log";
#else
        m_logPath = "/tmp/TuneLabBridge.log";
#endif
        
        m_file.open(m_logPath, std::ios::app);
        
        if (m_file.is_open())
        {
            log("INFO", "=== TuneLab Bridge VST3 Plugin Started ===");
            log("INFO", "Log file: " + m_logPath);
        }
    }
    
    ~Logger()
    {
        if (m_file.is_open())
        {
            log("INFO", "=== TuneLab Bridge VST3 Plugin Stopped ===");
            m_file.close();
        }
    }
    
    Logger(const Logger&) = delete;
    Logger& operator=(const Logger&) = delete;
    
    std::ofstream m_file;
    std::mutex m_mutex;
    std::string m_logPath;
};

// Convenience macros
#define LOG_INFO(msg) TuneLabBridge::Logger::getInstance().info(msg)
#define LOG_ERROR(msg) TuneLabBridge::Logger::getInstance().error(msg)
#define LOG_DEBUG(msg) TuneLabBridge::Logger::getInstance().debug(msg)

} // namespace TuneLabBridge
