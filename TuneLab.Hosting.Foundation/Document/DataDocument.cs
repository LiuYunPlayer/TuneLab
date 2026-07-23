namespace TuneLab.Foundation;

public class DataDocument : DataObject
{
    public event Action? StatusChanged;
    public override Head Head => mHead;
    public IReadOnlyList<HistoryEntry> History => mReadOnlyHistory;
    public int HistoryPosition => mHistoryPosition;

    public DataDocument()
    {
        mReadOnlyHistory = mHistory.AsReadOnly();
        mHead = AllocateHead();
    }

    public void Clear()
    {
        mHistory.Clear();
        mHistoryPosition = 0;
        mUncommittedCommands.Clear();
        mHead = AllocateHead();

        StatusChanged?.Invoke();
    }

    public override bool Pushable()
    {
        return mUncommittedCommands.IsEmpty();
    }

    public bool Undoable()
    {
        if (!mUncommittedCommands.IsEmpty())
            return false;

        return mHistoryPosition > 0;
    }

    public bool Redoable()
    {
        if (!mUncommittedCommands.IsEmpty())
            return false;

        return mHistoryPosition < mHistory.Count;
    }

    public override bool Commit()
    {
        return Commit(DefaultHistoryDescription);
    }

    public override bool Commit(string description, string? detail = null)
    {
        if (mUncommittedCommands.IsEmpty())
            return false;

        description = string.IsNullOrWhiteSpace(description) ? DefaultHistoryDescription : description;
        var commands = mUncommittedCommands.Select(command => command.Command).ToArray();
        var entry = new HistoryEntry(
            mUncommittedCommands[0].BeforeState,
            mUncommittedCommands.ConstLast().AfterState,
            description,
            detail,
            new CompositeCommand(commands));

        if (mHistoryPosition < mHistory.Count)
        {
            mHistory.RemoveRange(mHistoryPosition, mHistory.Count - mHistoryPosition);
        }

        mHistory.Add(entry);
        mHistoryPosition++;
        mUncommittedCommands.Clear();
        StatusChanged?.Invoke();

        return true;
    }

    public override bool Discard()
    {
        if (mUncommittedCommands.IsEmpty())
            return false;

        while (!mUncommittedCommands.IsEmpty())
        {
            int index = mUncommittedCommands.Count - 1;
            var command = mUncommittedCommands[index];
            command.Command.Undo();
            mHead = command.BeforeState;
            mUncommittedCommands.RemoveAt(index);
        }
        StatusChanged?.Invoke();

        return true;
    }

    public override bool DiscardTo(Head head)
    {
        if (mHead == head)
            return false;

        int firstCommandToDiscard = mUncommittedCommands.FindIndex(command => command.BeforeState == head);
        if (firstCommandToDiscard < 0)
            return false;

        for (int i = mUncommittedCommands.Count - 1; i >= firstCommandToDiscard; i--)
        {
            var command = mUncommittedCommands[i];
            command.Command.Undo();
            mHead = command.BeforeState;
            mUncommittedCommands.RemoveAt(i);
        }

        return true;
    }

    public override bool Undo()
    {
        return MoveToHistory(mHistoryPosition - 1);
    }

    public override bool Redo()
    {
        return MoveToHistory(mHistoryPosition + 1);
    }

    public bool MoveToHistory(int position)
    {
        if (!mUncommittedCommands.IsEmpty() || position < 0 || position > mHistory.Count || position == mHistoryPosition)
            return false;

        IDisposable? mergeScope = Math.Abs(position - mHistoryPosition) > 1
            ? MergeNotifyWithoutCommand()
            : null;

        try
        {
            while (mHistoryPosition > position)
            {
                UndoOneHistoryEntry();
            }

            while (mHistoryPosition < position)
            {
                RedoOneHistoryEntry();
            }

            return true;
        }
        finally
        {
            try
            {
                mergeScope?.Dispose();
            }
            finally
            {
                StatusChanged?.Invoke();
            }
        }
    }

    protected override void Push(ICommand command)
    {
        var state = AllocateHead();
        mUncommittedCommands.Add(new UncommittedCommand(command, mHead, state));
        mHead = state;
        StatusChanged?.Invoke();
    }

    void UndoOneHistoryEntry()
    {
        var entry = mHistory[mHistoryPosition - 1];
        entry.Command.Undo();
        mHead = entry.BeforeState;
        mHistoryPosition--;
    }

    void RedoOneHistoryEntry()
    {
        var entry = mHistory[mHistoryPosition];
        entry.Command.Redo();
        mHead = entry.State;
        mHistoryPosition++;
    }

    Head AllocateHead()
    {
        return new Head(checked(++mLastAllocatedHead));
    }

    sealed class UncommittedCommand(ICommand command, Head beforeState, Head afterState)
    {
        public ICommand Command { get; } = command;
        public Head BeforeState { get; } = beforeState;
        public Head AfterState { get; } = afterState;
    }

    readonly List<HistoryEntry> mHistory = new();
    readonly IReadOnlyList<HistoryEntry> mReadOnlyHistory;
    readonly List<UncommittedCommand> mUncommittedCommands = new();
    const string DefaultHistoryDescription = "Edit Project";
    Head mHead;
    int mHistoryPosition = 0;
    int mLastAllocatedHead = 0;
}
