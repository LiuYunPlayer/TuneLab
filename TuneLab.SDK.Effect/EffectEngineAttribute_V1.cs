using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Effect;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class EffectEngineAttribute_V1(string type) : Attribute
{
    public string Type { get; private set; } = type;
}
