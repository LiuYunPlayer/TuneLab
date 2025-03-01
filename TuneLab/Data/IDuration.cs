using TuneLab.Foundation.Event;

namespace TuneLab.Data;

internal interface IDuration
{
    IActionEvent DurationChanged { get; }
    double Duration { get; }
}
