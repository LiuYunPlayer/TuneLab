using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Event;

public interface INotifiableProperty<T>
{
    IActionEvent Modified { get; }
    IActionEvent WillModify { get; }
    T Value { get; set; }
}
