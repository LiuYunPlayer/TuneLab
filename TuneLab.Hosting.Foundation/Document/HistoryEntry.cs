namespace TuneLab.Foundation;

public sealed class HistoryEntry
{
    internal HistoryEntry(Head beforeState, Head state, string description, string? detail, ICommand command)
    {
        BeforeState = beforeState;
        State = state;
        Description = description;
        Detail = detail;
        Command = command;
    }

    public Head State { get; }
    public string Description { get; }
    public string? Detail { get; }

    internal Head BeforeState { get; }
    internal ICommand Command { get; }
}
