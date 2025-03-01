namespace TuneLab.Base.Data;

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
