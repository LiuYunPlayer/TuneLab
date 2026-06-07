using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.Primitives.DataStructures;

namespace TuneLab.SDK.Voice;

public interface IVoiceSource
{
    string Name { get; }
    string DefaultLyric { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    IReadOnlyOrderedMap<string, IControllerConfig> PartProperties { get; }
    IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties { get; }

    // 条件属性面板：宿主在属性 commit 时按当前值重算面板。默认回退到上面的静态声明（忽略 context）——
    // 想做"随其他字段值动态改变控件/字段"的插件覆写这两个方法，返回当前 context 下应呈现的 ObjectConfig。
    // 须为纯函数（同输入同输出、无副作用、轻量）：宿主在每次值 commit 时调用并 keyed-diff 到控件树。
    ObjectConfig GetPartConfig(IPropertyContext context) => new(PartProperties);
    ObjectConfig GetNoteConfig(IPropertyContext context) => new(NoteProperties);

    IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote;
    ISynthesisTask CreateSynthesisTask(ISynthesisData data);
}

public static class IVoiceSourceExtension
{
    public static IReadOnlyList<SynthesisSegment<T>> SimpleSegment<T>(this IVoiceSource voiceSource, SynthesisSegment<T> segment, double minNoteSpacing = 0, double maxPieceDuration = double.MaxValue) where T : ISynthesisNote
    {
        List<SynthesisSegment<T>> segments = new();
        using var it = segment.Notes.GetEnumerator();
        if (!it.MoveNext())
            return segments;

        List<T> currentSegment = new() { it.Current };

        while (it.MoveNext())
        {
            var currentNote = it.Current;
            var previousNote = currentSegment.Last();

            if (currentNote.Duration() > maxPieceDuration)
                continue;

            if (currentNote.EndTime - currentSegment.First().StartTime <= maxPieceDuration && currentNote.StartTime - previousNote.EndTime <= minNoteSpacing)
            {
                currentSegment.Add(currentNote);
                continue;
            }

            segments.Add(new SynthesisSegment<T>() { Notes = currentSegment });
            currentSegment = new() { currentNote };
        }

        segments.Add(new SynthesisSegment<T>() { Notes = currentSegment });

        return segments;
    }
}