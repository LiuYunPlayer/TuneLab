using System.Collections.Generic;
using System.Linq;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Voice;

internal interface IVoiceSource
{
    string Name { get; }
    string DefaultLyric { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    ObjectConfig PartProperties { get; }
    IReadOnlyList<IReadOnlyList<ISynthesisNote>> Segment<T>(IReadOnlyList<ISynthesisNote> segment) where T : ISynthesisNote;
    IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output);
}

internal static class IVoiceSourceExtension
{
    internal static IReadOnlyList<IReadOnlyList<ISynthesisNote>> SimpleSegment(this IVoiceSource voiceSource, IReadOnlyList<ISynthesisNote> segment, double minNoteSpacing = 0, double maxPieceDuration = double.MaxValue)
    {
        List<IReadOnlyList<ISynthesisNote>> segments = [];
        using var it = segment.GetEnumerator();
        if (!it.MoveNext())
            return segments;

        List<ISynthesisNote> currentSegment = [it.Current];

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

            segments.Add(currentSegment);
            currentSegment = [currentNote];
        }

        segments.Add(currentSegment);

        return segments;
    }
}