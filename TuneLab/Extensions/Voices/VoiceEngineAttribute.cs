using System;

namespace TuneLab.Extensions.Voices;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class VoiceEngineAttribute(string type) : Attribute
{
    public string Type { get; private set; } = type;
}
