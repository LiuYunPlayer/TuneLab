using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Event;

public interface IMergableEvent : IActionEvent
{
    void BeginMerge();
    void EndMerge();
}
