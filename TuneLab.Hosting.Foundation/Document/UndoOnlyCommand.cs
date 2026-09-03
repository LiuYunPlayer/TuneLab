using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

public class UndoOnlyCommand(Action undo) : IDataCommand
{
    public void Redo()
    {

    }

    public void Undo()
    {
        undo();
    }
}
