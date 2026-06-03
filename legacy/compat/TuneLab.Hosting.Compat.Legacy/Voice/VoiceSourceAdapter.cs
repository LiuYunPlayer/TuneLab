using System.Collections.Generic;
using System.Linq;
using LVoice = TuneLab.Extensions.Voices;
using VBase = TuneLab.SDK.Base;
using VVoice = TuneLab.SDK.Voice;
using PStruct = TuneLab.Primitives.DataStructures;
using TuneLab.Hosting.Compat.Legacy.Conversion;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把老 IVoiceSource 适配成 V1 IVoiceSource。Config 族懒转换 + 缓存（冷设置面）。
internal sealed class VoiceSourceAdapter(LVoice.IVoiceSource legacy) : VVoice.IVoiceSource
{
    public string Name => legacy.Name;
    public string DefaultLyric => legacy.DefaultLyric;

    public PStruct.IReadOnlyOrderedMap<string, VBase.AutomationConfig> AutomationConfigs
        => mAutomationConfigs ??= legacy.AutomationConfigs.ToV1AutomationMap();

    public PStruct.IReadOnlyOrderedMap<string, VBase.IControllerConfig> PartProperties
        => mPartProperties ??= legacy.PartProperties.ToV1ConfigMap();

    public PStruct.IReadOnlyOrderedMap<string, VBase.IControllerConfig> NoteProperties
        => mNoteProperties ??= legacy.NoteProperties.ToV1ConfigMap();

    // 分组：宿主传真实 note 类型 T，包装成老 note 喂老引擎；引擎按时序分组后，用包装上的输入下标取回真实 T。
    // 全程零强制类型转换（泛型实参即 LegacyNoteAdapter，下标回查从输入列表取 T）。
    public IReadOnlyList<VVoice.SynthesisSegment<T>> Segment<T>(VVoice.SynthesisSegment<T> segment) where T : VVoice.ISynthesisNote
    {
        var input = segment.Notes as IReadOnlyList<T> ?? segment.Notes.ToList();
        var cache = new NoteWrapperCache();
        var wrappers = new List<LegacyNoteAdapter>(input.Count);
        for (int i = 0; i < input.Count; i++)
        {
            var wrapper = cache.Wrap(input[i])!;
            wrapper.Index = i;
            wrappers.Add(wrapper);
        }

        var legacySegment = new LVoice.SynthesisSegment<LegacyNoteAdapter>
        {
            PartProperties = segment.PartProperties.ToLegacy(),
            Notes = wrappers,
        };

        var legacyResult = legacy.Segment(legacySegment);

        var result = new List<VVoice.SynthesisSegment<T>>(legacyResult.Count);
        foreach (var seg in legacyResult)
        {
            var notes = new List<T>();
            foreach (var wrapper in seg.Notes) // wrapper : LegacyNoteAdapter（泛型实参即此，无需 cast）
            {
                if (wrapper.Index >= 0 && wrapper.Index < input.Count)
                    notes.Add(input[wrapper.Index]);
            }
            result.Add(new VVoice.SynthesisSegment<T> { PartProperties = segment.PartProperties, Notes = notes });
        }
        return result;
    }

    public VVoice.ISynthesisTask CreateSynthesisTask(VVoice.ISynthesisData data)
    {
        var dataAdapter = new SynthesisDataAdapter(data);
        var task = legacy.CreateSynthesisTask(dataAdapter);
        return new SynthesisTaskAdapter(task);
    }

    PStruct.IReadOnlyOrderedMap<string, VBase.AutomationConfig>? mAutomationConfigs;
    PStruct.IReadOnlyOrderedMap<string, VBase.IControllerConfig>? mPartProperties;
    PStruct.IReadOnlyOrderedMap<string, VBase.IControllerConfig>? mNoteProperties;
}
