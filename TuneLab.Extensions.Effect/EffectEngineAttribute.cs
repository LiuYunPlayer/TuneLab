using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Effect;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class EffectEngineAttribute(string type) : Attribute
{
    public string Type { get; private set; } = type;
}
