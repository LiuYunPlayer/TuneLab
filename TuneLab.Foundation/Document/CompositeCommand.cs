namespace TuneLab.Foundation.Document;

internal class CompositeCommand : ICommand
{
    public CompositeCommand(IReadOnlyList<ICommand> commands)
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

    List<ICommand> mCommands = new();
}
