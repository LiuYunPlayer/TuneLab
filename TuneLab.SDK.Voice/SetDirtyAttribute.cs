using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Voice;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SetDirtyAttribute(DirtyType dirtyType) : Attribute
{
    public DirtyType DirtyType { get; private set; } = dirtyType;
}
