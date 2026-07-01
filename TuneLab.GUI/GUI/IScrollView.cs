using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI;

internal interface IScrollView
{
    IScrollAxis HorizontalAxis { get; }
    IScrollAxis VerticalAxis { get; }
}
