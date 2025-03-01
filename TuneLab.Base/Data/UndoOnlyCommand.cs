namespace TuneLab.Base.Data;

public class UndoOnlyCommand(Action undo) : ICommand
{
    public void Redo()
    {

    }

    public void Undo()
    {
        undo();
    }
}
