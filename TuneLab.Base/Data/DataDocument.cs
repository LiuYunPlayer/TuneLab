using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Data;

public class DataDocument : DataObject
{
    public event Action? StatusChanged;
    public override Head Head => new(mCommitedCommands.Count + mUncommitedCommands.Count);
    
    public DataDocument() { }

    public void Clear()
    {
        mCommitedCommands.Clear();
        mRedoCommands.Clear();
        mUncommitedCommands.Clear();

        StatusChanged?.Invoke();
    }

    public bool Undoable()
    {
        if (!mUncommitedCommands.IsEmpty())
            return false;

        if (mCommitedCommands.IsEmpty())
            return false;

        return true;
    }

    public bool Redoable()
    {
        if (!mUncommitedCommands.IsEmpty())
            return false;

        if (mRedoCommands.IsEmpty())
            return false;

        return true;
    }

    public override bool Commit()
    {
        if (mUncommitedCommands.IsEmpty())
            return false;

        mCommitedCommands.Push(new CompositeCommand(mUncommitedCommands));
        mUncommitedCommands.Clear();
        mRedoCommands.Clear();
        StatusChanged?.Invoke();

        return true;
    }

    public override bool Discard()
    {
        if (mUncommitedCommands.IsEmpty())
            return false;

        for (int i = mUncommitedCommands.Count - 1; i >= 0; i--)
        {
            mUncommitedCommands[i].Undo();
        }
        mUncommitedCommands.Clear();
        StatusChanged?.Invoke();

        return true;
    }

    public override bool DiscardTo(Head head)
    {
        if (Head == head)
            return false;

        // TODO: 优化，预先检查未提交的改动中是否包含传入的head
        while (!mUncommitedCommands.IsEmpty())
        {
            mUncommitedCommands.ConstLast().Undo();
            mUncommitedCommands.RemoveAt(mUncommitedCommands.Count - 1);
            if (Head == head)
                break;
        }

        return true;
    }

    public override bool Undo()
    {
        if (!Undoable())
            return false;

        var command = mCommitedCommands.Pop();
        command.Undo();
        mRedoCommands.Push(command);
        StatusChanged?.Invoke();

        return true;
    }

    public override bool Redo()
    {
        if (!Redoable())
            return false;

        var command = mRedoCommands.Pop();
        command.Redo();
        mCommitedCommands.Push(command);
        StatusChanged?.Invoke();

        return true;
    }

    protected override void Push(ICommand command)
    {
        mUncommitedCommands.Add(command);
        StatusChanged?.Invoke();
    }

    readonly Stack<ICommand> mCommitedCommands = new();
    readonly Stack<ICommand> mRedoCommands = new();
    readonly List<ICommand> mUncommitedCommands = new();
}
