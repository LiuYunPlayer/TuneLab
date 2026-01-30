/*
 * MidiProcessor.cpp - MIDI processing utilities
 */

#include <cstdint>
#include <cmath>
#include <vector>

namespace PluginHost
{
namespace MidiUtils
{

// MIDI Status bytes
constexpr uint8_t NOTE_OFF = 0x80;
constexpr uint8_t NOTE_ON = 0x90;
constexpr uint8_t POLY_AFTERTOUCH = 0xA0;
constexpr uint8_t CONTROL_CHANGE = 0xB0;
constexpr uint8_t PROGRAM_CHANGE = 0xC0;
constexpr uint8_t CHANNEL_AFTERTOUCH = 0xD0;
constexpr uint8_t PITCH_BEND = 0xE0;
constexpr uint8_t SYSTEM_EXCLUSIVE = 0xF0;

// Common CC numbers
constexpr uint8_t CC_BANK_SELECT_MSB = 0;
constexpr uint8_t CC_MODULATION = 1;
constexpr uint8_t CC_BREATH = 2;
constexpr uint8_t CC_FOOT_CONTROLLER = 4;
constexpr uint8_t CC_PORTAMENTO_TIME = 5;
constexpr uint8_t CC_DATA_ENTRY_MSB = 6;
constexpr uint8_t CC_VOLUME = 7;
constexpr uint8_t CC_BALANCE = 8;
constexpr uint8_t CC_PAN = 10;
constexpr uint8_t CC_EXPRESSION = 11;
constexpr uint8_t CC_BANK_SELECT_LSB = 32;
constexpr uint8_t CC_SUSTAIN = 64;
constexpr uint8_t CC_PORTAMENTO = 65;
constexpr uint8_t CC_SOSTENUTO = 66;
constexpr uint8_t CC_SOFT_PEDAL = 67;
constexpr uint8_t CC_LEGATO = 68;
constexpr uint8_t CC_HOLD_2 = 69;
constexpr uint8_t CC_ALL_SOUND_OFF = 120;
constexpr uint8_t CC_RESET_ALL_CONTROLLERS = 121;
constexpr uint8_t CC_LOCAL_CONTROL = 122;
constexpr uint8_t CC_ALL_NOTES_OFF = 123;
constexpr uint8_t CC_OMNI_MODE_OFF = 124;
constexpr uint8_t CC_OMNI_MODE_ON = 125;
constexpr uint8_t CC_MONO_MODE_ON = 126;
constexpr uint8_t CC_POLY_MODE_ON = 127;

/**
 * Check if status byte is a channel message
 */
bool isChannelMessage(uint8_t status)
{
    return status >= 0x80 && status < 0xF0;
}

/**
 * Get the channel from a status byte
 */
uint8_t getChannel(uint8_t status)
{
    return status & 0x0F;
}

/**
 * Get the message type from a status byte
 */
uint8_t getMessageType(uint8_t status)
{
    return status & 0xF0;
}

/**
 * Create a note on message
 */
void makeNoteOn(uint8_t channel, uint8_t note, uint8_t velocity,
                uint8_t& status, uint8_t& data1, uint8_t& data2)
{
    status = NOTE_ON | (channel & 0x0F);
    data1 = note & 0x7F;
    data2 = velocity & 0x7F;
}

/**
 * Create a note off message
 */
void makeNoteOff(uint8_t channel, uint8_t note, uint8_t velocity,
                 uint8_t& status, uint8_t& data1, uint8_t& data2)
{
    status = NOTE_OFF | (channel & 0x0F);
    data1 = note & 0x7F;
    data2 = velocity & 0x7F;
}

/**
 * Create a control change message
 */
void makeControlChange(uint8_t channel, uint8_t controller, uint8_t value,
                        uint8_t& status, uint8_t& data1, uint8_t& data2)
{
    status = CONTROL_CHANGE | (channel & 0x0F);
    data1 = controller & 0x7F;
    data2 = value & 0x7F;
}

/**
 * Create a pitch bend message
 * @param value Pitch bend value (0-16383, center = 8192)
 */
void makePitchBend(uint8_t channel, int value,
                   uint8_t& status, uint8_t& data1, uint8_t& data2)
{
    status = PITCH_BEND | (channel & 0x0F);
    data1 = value & 0x7F;
    data2 = (value >> 7) & 0x7F;
}

/**
 * Create a program change message
 */
void makeProgramChange(uint8_t channel, uint8_t program,
                        uint8_t& status, uint8_t& data1, uint8_t& data2)
{
    status = PROGRAM_CHANGE | (channel & 0x0F);
    data1 = program & 0x7F;
    data2 = 0;
}

/**
 * Convert pitch bend value (-8192 to 8191) to MIDI range (0-16383)
 */
int pitchBendToMidi(int value)
{
    return value + 8192;
}

/**
 * Convert MIDI pitch bend (0-16383) to signed value (-8192 to 8191)
 */
int midiToPitchBend(int midiValue)
{
    return midiValue - 8192;
}

/**
 * Convert MIDI note number to frequency (Hz)
 */
double noteToFrequency(int note, double A4Frequency = 440.0)
{
    return A4Frequency * std::pow(2.0, (note - 69) / 12.0);
}

/**
 * Convert frequency (Hz) to MIDI note number (fractional)
 */
double frequencyToNote(double frequency, double A4Frequency = 440.0)
{
    return 69.0 + 12.0 * std::log2(frequency / A4Frequency);
}

/**
 * Get note name from MIDI note number
 */
const char* getNoteName(int note)
{
    static const char* names[] = {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
    };
    return names[note % 12];
}

/**
 * Get octave from MIDI note number
 */
int getNoteOctave(int note)
{
    return (note / 12) - 1;
}

} // namespace MidiUtils
} // namespace PluginHost
