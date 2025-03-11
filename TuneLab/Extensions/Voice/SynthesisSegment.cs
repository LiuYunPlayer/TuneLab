using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;

namespace TuneLab.Extensions.Voice;

public struct SynthesisSegment
{
    public PropertyObject PartProperties;
    public IReadOnlyCollection<ISynthesisNote> Notes;

    public bool EqualsWith(SynthesisSegment other)
    {
        return Notes.SequenceEqual(other.Notes);
    }
}
