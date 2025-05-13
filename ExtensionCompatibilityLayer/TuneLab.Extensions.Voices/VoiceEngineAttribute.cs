using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Voices;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class VoiceEngineAttribute(string type) : Attribute
{
    public string Type { get; private set; } = type;
}
