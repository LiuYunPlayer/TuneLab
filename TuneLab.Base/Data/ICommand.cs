namespace TuneLab.Base.Data;

public interface ICommand
{
    public void Undo();
    public void Redo();
}
