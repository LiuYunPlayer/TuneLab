using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Core.DataInfo;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface IVoice : IVoiceSynthesisContext, IDataObject<VoiceInfo>
{
    string Name { get; }
    string DefaultLyric { get; }
    string Type { get; }
    ObjectConfig GetNotePropertyConfig(IEnumerable<INote> notes);
    IEnumerable<IReadOnlyList<INote>> Segment(IEnumerable<INote> notes);
    IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output);
}

internal static class IVoiceExtensions
{
    public static IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(this IVoice voice)
    {
        return VoiceManager.GetAutomationConfig(voice.Type, [voice]);
    }

    public static bool IsEffectiveAutomation(this IVoice voice, string id)
    {
        var configs = voice.GetAutomationConfigs();
        return configs.ContainsKey(id);
    }

    public static bool TryGetAutomationConfig(this IVoice voice, string id, [NotNullWhen(true)][MaybeNullWhen(false)] out AutomationConfig? config)
    {
        var configs = voice.GetAutomationConfigs();
        return configs.TryGetValue(id, out config);
    }
}