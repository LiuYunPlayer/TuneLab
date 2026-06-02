using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LProp = TuneLab.Base.Properties;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK.Voice;
using TuneLab.Hosting.Compat.Legacy.Conversion;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把宿主 V1 ISynthesisData 适配成老 ISynthesisData 喂给老引擎的 CreateSynthesisTask。
// 持有 NoteWrapperCache（note 身份缓存），经它包装的 note 被老引擎用作 phonemes 键时可经 .Origin 映射回宿主。
internal sealed class SynthesisDataAdapter(VVoice.ISynthesisData data) : LVoice.ISynthesisData
{
    public NoteWrapperCache NoteCache { get; } = new();

    public IEnumerable<LVoice.ISynthesisNote> Notes => data.Notes.Select(n => (LVoice.ISynthesisNote)NoteCache.Wrap(n)!);
    public LProp.PropertyObject PartProperties => mPartProperties ??= data.PartProperties.ToLegacy();
    public LVoice.IAutomationValueGetter Pitch => mPitch ??= new AutomationValueGetterAdapter(data.Pitch);

    public bool GetAutomation(string automationID, [MaybeNullWhen(false)][NotNullWhen(true)] out LVoice.IAutomationValueGetter? automation)
    {
        if (data.GetAutomation(automationID, out var v1) && v1 != null)
        {
            automation = new AutomationValueGetterAdapter(v1);
            return true;
        }
        automation = null;
        return false;
    }

    LProp.PropertyObject? mPartProperties;
    LVoice.IAutomationValueGetter? mPitch;
}

// 自动化取值器：双向签名一致（double[] GetValue(IReadOnlyList<double>)），直接转发。
internal sealed class AutomationValueGetterAdapter(VVoice.IAutomationValueGetter v1) : LVoice.IAutomationValueGetter
{
    public double[] GetValue(IReadOnlyList<double> times) => v1.GetValue(times);
}
