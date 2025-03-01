using System.Collections.Generic;
using System.Linq;
using TuneLab.Base.Properties;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Voices;

public interface IVoiceSource
{
    string Name { get; }
    string DefaultLyric { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    IReadOnlyOrderedMap<string, IPropertyConfig> PartProperties { get; }
    IReadOnlyOrderedMap<string, IPropertyConfig> NoteProperties { get; }
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