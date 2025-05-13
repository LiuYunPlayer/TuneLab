﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
