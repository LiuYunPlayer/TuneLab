namespace TuneLab.Foundation.Document;

public class RedoOnlyCommand(Action redo) : ICommand
{
    public void Redo()
    {
        redo();
    }

    public void Undo()
    {

    }
}
