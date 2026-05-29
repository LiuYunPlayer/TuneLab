using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Science;

public interface ITempoHelper
{
    public double Pos { get; }
    public double Bpm { get; }
    public double Time { get; }
    public double Coe { get; }
}
