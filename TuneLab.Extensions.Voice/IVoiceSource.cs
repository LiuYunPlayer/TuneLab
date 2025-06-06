using System.Collections.Generic;
using System.Linq;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Voice;

public interface IVoiceSource
{
    string DefaultLyric { get; }
    ObjectConfig GetNotePropertyConfig(IEnumerable<ISynthesisNote> notes);
    IEnumerable<IReadOnlyList<ISynthesisNote>> Segment(IEnumerable<ISynthesisNote> notes);
    IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output);
}

public static class IVoiceSourceExtension
{
    public static IEnumerable<IReadOnlyList<ISynthesisNote>> SimpleSegment(this IVoiceSource voiceSource, IEnumerable<ISynthesisNote> notes, double minNoteSpacing = 0, double maxPieceDuration = double.MaxValue)
    {
        List<IReadOnlyList<ISynthesisNote>> segments = [];
        using var it = notes.GetEnumerator();
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