using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

    class AutomationAnchorItem(AutomationRenderer automationRenderer) : AutomationRenderItem(automationRenderer)
    {
        public required IAutomation Automation { get; set; }
        public required AnchorPoint AnchorPoint { get; set; }
        public required double MinValue { get; set; }
        public required double MaxValue { get; set; }
        public required Color Color { get; set; }

        public Point Position()
        {
            double value = AnchorPoint.Value + Automation.DefaultValue.Value;
            return AutomationRenderer.TickAndValueToPoint(AnchorPoint.Pos, value, MinValue, MaxValue);
        }

        public override bool Raycast(Point point)
        {
            return Point.Distance(Position(), point) <= 6;
        }

        public override void Render(DrawingContext context)
        {
            var hoverAnchor = (AutomationRenderer.HoverItem() as AutomationAnchorItem)?.AnchorPoint;
            var center = Position();
            var pointBrush = new SolidColorBrush(Color);
            context.DrawEllipse(pointBrush, null, center, 2.5, 2.5);
            if (AnchorPoint.IsSelected)
            {
                context.DrawEllipse(null, new Pen(pointBrush), center, 5.5, 5.5);
            }
            else if (AnchorPoint == hoverAnchor)
            {
                context.DrawEllipse(null, new Pen(Brushes.White), center, 5.5, 5.5);
            }
        }
    }
}
