using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

// 默认实现：一对 redo/undo 委托，两支都有动作。只需单支的用 UndoOnlyCommand / RedoOnlyCommand。
public class DataCommand(Action redo, Action undo) : IDataCommand
{
    public void Redo()
    {
        redo();
    }

    public void Undo()
    {
        undo();
    }
}
