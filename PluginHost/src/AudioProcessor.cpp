/*
 * AudioProcessor.cpp - Audio processing utilities
 * 
 * This file contains utility functions for audio processing.
 * The main audio processing logic is in PluginInstance.
 */

#include <cstring>
#include <cmath>
#include <algorithm>

namespace PluginHost
{
namespace AudioUtils
{

/**
 * Interleave audio buffers
 */
void interleave(const float** source, float* dest, int numChannels, int numSamples)
{
    for (int s = 0; s < numSamples; ++s)
    {
        for (int ch = 0; ch < numChannels; ++ch)
        {
            dest[s * numChannels + ch] = source[ch][s];
        }
    }
}

/**
 * Deinterleave audio buffers
 */
void deinterleave(const float* source, float** dest, int numChannels, int numSamples)
{
    for (int s = 0; s < numSamples; ++s)
    {
        for (int ch = 0; ch < numChannels; ++ch)
        {
            dest[ch][s] = source[s * numChannels + ch];
        }
    }
}

/**
 * Apply gain to audio buffer
 */
void applyGain(float* buffer, int numSamples, float gain)
{
    for (int i = 0; i < numSamples; ++i)
    {
        buffer[i] *= gain;
    }
}

/**
 * Mix two buffers together
 */
void mix(const float* source, float* dest, int numSamples, float gain)
{
    for (int i = 0; i < numSamples; ++i)
    {
        dest[i] += source[i] * gain;
    }
}

/**
 * Clear audio buffer
 */
void clear(float* buffer, int numSamples)
{
    std::memset(buffer, 0, numSamples * sizeof(float));
}

/**
 * Copy audio buffer
 */
void copy(const float* source, float* dest, int numSamples)
{
    std::memcpy(dest, source, numSamples * sizeof(float));
}

/**
 * Clip audio to range [-1, 1]
 */
void clip(float* buffer, int numSamples)
{
    for (int i = 0; i < numSamples; ++i)
    {
        buffer[i] = std::max(-1.0f, std::min(1.0f, buffer[i]));
    }
}

/**
 * Get peak level
 */
float getPeakLevel(const float* buffer, int numSamples)
{
    float peak = 0.0f;
    for (int i = 0; i < numSamples; ++i)
    {
        float absValue = std::fabs(buffer[i]);
        if (absValue > peak)
            peak = absValue;
    }
    return peak;
}

/**
 * Convert linear amplitude to decibels
 */
float linearToDb(float linear)
{
    if (linear <= 0.0f)
        return -120.0f;
    return 20.0f * std::log10(linear);
}

/**
 * Convert decibels to linear amplitude
 */
float dbToLinear(float db)
{
    return std::pow(10.0f, db / 20.0f);
}

} // namespace AudioUtils
} // namespace PluginHost
