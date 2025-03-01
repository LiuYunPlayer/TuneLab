namespace TuneLab.Foundation.Document;

public interface ICommand
{
    public void Undo();
    public void Redo();
}
