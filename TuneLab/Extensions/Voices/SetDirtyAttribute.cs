using System;

namespace TuneLab.Extensions.Voices;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SetDirtyAttribute(DirtyType dirtyType) : Attribute
{
    public DirtyType DirtyType { get; private set; } = dirtyType;
}
