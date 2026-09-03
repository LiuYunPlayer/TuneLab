using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

public class RedoOnlyCommand(Action redo) : IDataCommand
{
    public void Redo()
    {
        redo();
    }

    public void Undo()
    {

    }
}
