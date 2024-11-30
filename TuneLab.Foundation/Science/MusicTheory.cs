using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Science;

public static class MusicTheory
{
    public enum QuantizationBase
    {
        Base_1 = 1,
        Base_3 = 3,
        Base_5 = 5
    }

    public enum QuantizationDivision
    {
        Division_1 = 1,
        Division_2 = 2,
        Division_4 = 4,
        Division_8 = 8,
        Division_16 = 16,
        Division_32 = 32,
    }

    // MIDI
    public const int RESOLUTION = 480;

    // PITCH
    public static readonly double A4_FREQUENCY = 440;
    public const int A4_PITCH = 69;

    public const int MIN_PITCH = 0;
    public const int MAX_PITCH = 127;
    public const int PITCH_COUNT = MAX_PITCH - MIN_PITCH + 1;

    public const int C0_PITCH = A4_PITCH - 48 - 9;

    public static double PitchToFrequency(double pitch)
    {
        return A4_FREQUENCY * Math.Pow(ONE_TWELFTH_POW_OF_2, pitch - A4_PITCH);
    }
    public static double FrequencyToPitch(double frequency)
    {
        return A4_PITCH + Math.Log(frequency / A4_FREQUENCY) * LOG2_DIVIDE_12;
    }
    public static bool IsWhite(int pitch)
    {
        int mod = MathUtility.PositiveMod(pitch - C0_PITCH, 12);
        return mod == 0 ||
               mod == 2 ||
               mod == 4 ||
               mod == 5 ||
               mod == 7 ||
               mod == 9 ||
               mod == 11;
    }
    public static bool IsBlack(int pitch)
    {
        int mod = MathUtility.PositiveMod(pitch - C0_PITCH, 12);
        return mod == 1 ||
               mod == 3 ||
               mod == 6 ||
               mod == 8 ||
               mod == 10;
    }
    public static bool IsEorB(int pitch)
    {
        int mod = MathUtility.PositiveMod(pitch - C0_PITCH, 12);
        return mod == 4 ||
               mod == 11;
    }

    static readonly double ONE_TWELFTH_POW_OF_2 = Math.Pow(2, 1.0 / 12);
    static readonly double LOG2_DIVIDE_12 = 12 / Math.Log(2);

    //Gain
    public static double dB2Level(double dB)
    {
        return Math.Pow(10, dB / 20);
    }
}
