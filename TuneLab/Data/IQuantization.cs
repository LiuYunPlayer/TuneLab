using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Science;

namespace TuneLab.Data;

internal interface IQuantization
{
    event Action QuantizationChanged;
    MusicTheory.QuantizationBase Base { get; set; }
    MusicTheory.QuantizationDivision Division { get; set; }
}

internal static class IQuantizationExtension
{
    public static int Level(this IQuantization quantization)
    {
        return (int)quantization.Base * (int)quantization.Division;
    }

    public static int TicksPerCell(this IQuantization quantization)
    {
        return MusicTheory.RESOLUTION / quantization.Level();
    }

    public static void Set(this IQuantization quantization, MusicTheory.QuantizationBase quantizationBase, MusicTheory.QuantizationDivision quantizationDivision)
    {
        quantization.Base = quantizationBase;
        quantization.Division = quantizationDivision;
    }

    public static void Set(this IQuantization quantization, int level)
    {
        switch (level)
        {
            case 1:
                quantization.Base = MusicTheory.QuantizationBase.Base_1;
                quantization.Division = MusicTheory.QuantizationDivision.Division_1;
                break;
            case 2:
                quantization.Base = MusicTheory.QuantizationBase.Base_1;
                quantization.Division = MusicTheory.QuantizationDivision.Division_2;
                break;
            case 3:
                quantization.Base = MusicTheory.QuantizationBase.Base_3;
                quantization.Division = MusicTheory.QuantizationDivision.Division_1;
                break;
            case 4:
                quantization.Base = MusicTheory.QuantizationBase.Base_1;
                quantization.Division = MusicTheory.QuantizationDivision.Division_4;
                break;
            case 5:
                quantization.Base = MusicTheory.QuantizationBase.Base_5;
                quantization.Division = MusicTheory.QuantizationDivision.Division_1;
                break;
                // TODO
        }
    }
}
