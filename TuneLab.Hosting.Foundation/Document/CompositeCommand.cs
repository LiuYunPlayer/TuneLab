using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

internal class CompositeCommand : IDataCommand
{
    public CompositeCommand(IReadOnlyList<IDataCommand> commands)
    {
        foreach (var command in commands)
        {
            mCommands.Add(command);
        }
    }

    public void Redo()
    {
        for (int i = 0; i < mCommands.Count; i++)
        {
            mCommands[i].Redo();
        }
    }

    public void Undo()
    {
        for (int i = mCommands.Count - 1; i >= 0; i--)
        {
            mCommands[i].Undo();
        }
    }

    List<IDataCommand> mCommands = new();
}
