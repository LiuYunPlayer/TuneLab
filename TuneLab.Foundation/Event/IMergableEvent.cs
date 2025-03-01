namespace TuneLab.Foundation.Event;

public interface IMergableEvent : IActionEvent
{
    void BeginMerge();
    void EndMerge();
}
