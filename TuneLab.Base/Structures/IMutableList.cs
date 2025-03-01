namespace TuneLab.Base.Structures;

public interface IMutableList<T> : IList<T>, IReadOnlyList<T>
{
    new int Count { get; }
    new T this[int index] { get; set; }

    int ICollection<T>.Count => Count;
    T IList<T>.this[int index] { get => this[index]; set => this[index] = value; }

    int IReadOnlyCollection<T>.Count => Count;
    T IReadOnlyList<T>.this[int index] => this[index];
}
