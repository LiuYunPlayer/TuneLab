using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Document;

public class Command(Action redo, Action undo) : ICommand
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
