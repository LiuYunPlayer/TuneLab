namespace TuneLab.Base.Event;

public interface IMergableEvent : IActionEvent
{
    void BeginMerge();
    void EndMerge();
}
