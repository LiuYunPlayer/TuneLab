using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;

namespace TuneLab.Data;

internal interface IPlayhead
{
    IActionEvent PosChanged { get; }
    double Pos { get; set; }
}
