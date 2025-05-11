using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Effect;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class EffectEngineAttribute(string type) : Attribute
{
    public string Type { get; } = type;
}
