using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Data;

namespace TuneLab.Views;

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
