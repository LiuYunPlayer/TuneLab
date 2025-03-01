namespace TuneLab.Base.Structures;

public interface ILinkedNode<T> where T : class, ILinkedNode<T>
{
    T? Next { get; set; }
    T? Last { get; set; }
    ILinkedList<T>? LinkedList { get; set; }
}
