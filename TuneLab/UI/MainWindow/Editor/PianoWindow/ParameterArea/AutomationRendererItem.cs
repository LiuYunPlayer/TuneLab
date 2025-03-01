using Avalonia;
using TuneLab.Data;

namespace TuneLab.UI;

internal partial class AutomationRenderer
{
    class AutomationRenderItem(AutomationRenderer automationRenderer) : Item
    {
        public AutomationRenderer AutomationRenderer => automationRenderer;
    }

    class VibratoItem(AutomationRenderer automationRenderer) : AutomationRenderItem(automationRenderer)
    {
        public required Vibrato Vibrato;

        public override bool Raycast(Point point)
        {
            double left = AutomationRenderer.TickAxis.Tick2X(Vibrato.GlobalStartPos());
            double right = AutomationRenderer.TickAxis.Tick2X(Vibrato.GlobalEndPos());
            return point.X > left && point.X < right;
        }
    }
}
