using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.VoiceConflictB;

// 冲突消解夹具 B（voice）：引擎 id TLConflictVoice（与包 A 同身份、不同包 id）。
// 极简引擎：暴露一个声源，名字标注「Package B」——活实现是哪个包，Set Voice 菜单里的声源名即见分晓。
// 会话不产音频（合成静音）：路由/分组测试只需能区分活实现，不需要真实合成。
public sealed class ConflictVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    public void Init()
        => mVoiceInfos.Add("conflict-voice", new VoiceSourceInfo { Name = "Conflict Voice (Package B)", Description = "From package B" });

    public void Destroy() { }

    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context) => new SilentSession();

    // 声明（引擎层、纯函数、全空）：路由/分组测试不需要轨/面板。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context) => sEmptyAutomations;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context) => sEmptyAutomations;
    public ObjectConfig GetPartPropertyConfig(IPartPropertyContext context) => sEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(INotePropertyContext context) => sEmptyConfig;

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
    static readonly ObjectConfig sEmptyConfig = new() { Properties = new OrderedMap<PropertyKey, IControllerConfig>() };
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyAutomations = new();
}

// 静音会话：不调度任何段。够路由/分组测试用。
internal sealed class SilentSession : ISynthesisSession
{
    public string DefaultLyric => "la";

    public SynthesisRange? GetNextSegment(double startTime, double endTime) => null;
    public Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default) => Task.CompletedTask;

    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => Array.Empty<IReadOnlyList<Point>>();
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => sEmptyParameters;
    public IReadOnlyList<SynthesizedPhoneme> Phonemes => Array.Empty<SynthesizedPhoneme>();

    public IReadOnlyList<SynthesisStatusSegment> GetStatus() => Array.Empty<SynthesisStatusSegment>();
    public event Action? StatusChanged { add { } remove { } }

    public void Dispose() { }

    static readonly ObjectConfig sEmptyConfig = new() { Properties = new OrderedMap<PropertyKey, IControllerConfig>() };
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyAutomations = new();
    static readonly Map<string, SynthesizedParameter> sEmptyParameters = new();
}
