using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.Primitives.DataStructures;

namespace TuneLab.SDK.Voice;

public interface ISynthesisData
{
    IEnumerable<ISynthesisNote> Notes { get; }
    PropertyObject PartProperties { get; }
    bool GetAutomation(string automationID, [MaybeNullWhen(false)] [NotNullWhen(true)] out IAutomationValueGetter? automation);
    IAutomationValueGetter Pitch { get; }
}

public static class ISynthesisDataExtension
{
    public static double StartTime(this ISynthesisData data)
    {
        return data.Notes.First().StartTime;
    }

    public static double EndTime(this ISynthesisData data)
    {
        return data.Notes.Last().EndTime;
    }
}
