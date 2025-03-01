using TuneLab.Base.Properties;

namespace TuneLab.Extensions.Voices;

public struct SynthesisSegment<T> where T : ISynthesisNote
{
    public PropertyObject PartProperties;
    public IReadOnlyCollection<T> Notes;

    public bool EqualsWith(SynthesisSegment<T> other)
    {
        return Notes.SequenceEqual(other.Notes);
    }
}
