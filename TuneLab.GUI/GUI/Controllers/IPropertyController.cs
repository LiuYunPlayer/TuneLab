using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI.Controllers;

internal interface IPropertyController
{
    void Terminate();
}

internal interface IPropertyController<T> : IPropertyController
{
    void Setup(T value);
}
