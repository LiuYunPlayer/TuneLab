using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal interface IAutomation : IDataObject<AutomationInfo>
{
    IMidiPart Part { get; }
    IActionEvent<double, double> RangeModified { get; }
    IDataProperty<double> DefaultValue { get; }
    IReadOnlyList<Point> Points { get; }
    double[] GetValues(IReadOnlyList<double> ticks);
    void AddLine(IReadOnlyList<Point> points, double extend);
    void Clear(double start, double end, double extend);
}

internal static class IAutomationExtension
{
    public static double GetValue(this IAutomation automation, double tick)
    {
        return automation.GetValues([tick])[0];
    }

    public static List<Point> RangeInfo(this IAutomation automation, double start, double end)
    {
        List<Point> result = new List<Point>();

        result.Add(new Point(0, automation.GetValue(start) - automation.DefaultValue.Value));
        foreach (var point in automation.Points)
        {
            if (point.X <= start)
                continue;

            if (point.X >= end)
                break;

            result.Add(new(point.X - start, point.Y));
        }
        result.Add(new Point(end - start, automation.GetValue(end) - automation.DefaultValue.Value));

        return result;
    }
}
