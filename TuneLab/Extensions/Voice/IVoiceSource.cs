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
    ObjectConfig NoteProperties { get; }
    IReadOnlyList<SynthesisSegment> Segment<T>(SynthesisSegment segment) where T : ISynthesisNote;
    IVoiceSynthesisTask CreateSynthesisTask(IVoiceSynthesisInput input, IVoiceSynthesisOutput output);
}

internal static class IVoiceSourceExtension
{
    internal static IReadOnlyList<SynthesisSegment> SimpleSegment(this IVoiceSource voiceSource, SynthesisSegment segment, double minNoteSpacing = 0, double maxPieceDuration = double.MaxValue)
    {
        List<SynthesisSegment> segments = new();
        using var it = segment.Notes.GetEnumerator();
        if (!it.MoveNext())
            return segments;

        List<ISynthesisNote> currentSegment = new() { it.Current };

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

            segments.Add(new SynthesisSegment() { Notes = currentSegment });
            currentSegment = [currentNote];
        }

        segments.Add(new SynthesisSegment() { Notes = currentSegment });

        return segments;
    }
}