using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI.Components;

internal interface IComponent
{
    public bool IsHover { get; }
    public bool IsPrimaryButtonDragging { get; }
    public bool IsMiddleButtonDragging { get; }
    public bool IsSecondaryButtonDragging { get; }
    public bool IsPressed { get; }
    public long DoubleClickInterval { get; set; }
}
