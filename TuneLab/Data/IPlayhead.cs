using TuneLab.Foundation.Event;

namespace TuneLab.Data;

internal interface IPlayhead
{
    IActionEvent PosChanged { get; }
    double Pos { get; set; }
}
