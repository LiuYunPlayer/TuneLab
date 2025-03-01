using System;
using TuneLab.Foundation.Science;

namespace TuneLab.Data;

internal class Quantization : IQuantization
{
    public event Action? QuantizationChanged;
    public MusicTheory.QuantizationBase Base { get => mBase; set { mBase = value; QuantizationChanged?.Invoke(); } }
    public MusicTheory.QuantizationDivision Division { get => mDivision; set { mDivision = value; QuantizationChanged?.Invoke(); } }

    public Quantization(MusicTheory.QuantizationBase quantizationBase, MusicTheory.QuantizationDivision quantizationDivision)
    {
        Base = quantizationBase;
        Division = quantizationDivision;
    }

    MusicTheory.QuantizationBase mBase;
    MusicTheory.QuantizationDivision mDivision;
}
