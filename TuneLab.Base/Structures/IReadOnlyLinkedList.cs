﻿namespace TuneLab.Base.Structures;

public interface IReadOnlyLinkedList<out T> : IReadOnlyCollection<T>
{
    T? Begin { get; }
    T? End { get; }
    IEnumerator<T> Inverse();
}
