using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Extensions.Effect;
using System.Reactive.Linq;
using TuneLab.Data.Synthesis;
using TuneLab.Utils;
using System.Threading;

namespace TuneLab.Data;

internal class MidiPart : Part, IMidiPart
{
    public IActionEvent SynthesisStatusChanged => mSynthesisStatusChanged;
    // 自动化轨集合因参数 commit 而变（voice 依赖 part 参数、各 effect 依赖自身参数）：UI 收到重建参数栏/默认值面板。
    // 仅在轨集合实际变化时触发（签名比对去抖）；换源/链增删走各自既有 UI 触发，不重复经此事件。
    public IActionEvent AutomationConfigsModified => mAutomationConfigsModified;
    public override DataString Name { get; }
    public override DataStruct<double> Pos { get; }
    public override DataStruct<double> Dur { get; }
    public DataStruct<double> Gain { get; }
    public DataPropertyObject Properties { get; }
    public INoteList Notes => mNotes;
    public IVoice Voice => mVoice;
    IDataProperty<double> IMidiPart.Gain => Gain;
    public IReadOnlyDataObjectMap<string, IAutomation> Automations => mAutomations;
    // 声明分段轨（除 Pitch 外、声源声明的可编辑分段曲线，即 AutomationConfig.IsPiecewise 的轨），按轨 id 存。
    // Pitch 是 part 专属常驻通道、在音符区编辑，不入此 map。
    public IReadOnlyDataObjectMap<string, IPiecewiseAutomation> PiecewiseAutomations => mPiecewiseAutomations;
    public IReadOnlyDataObjectList<IEffect> Effects => mEffects;
    public IPiecewiseAutomation Pitch => mPitchLine;
    public IReadOnlyDataObjectList<Vibrato> Vibratos => mVibratos;

    // —— 合成消费面（session 模型）：状态/产物全由插件托管，宿主经管线包装拉取展示 ——
    public VoiceSynthesisPipeline? SynthesisPipeline => mPipeline;
    public bool IsSynthesisBatching => mSynthesisBatch.IsBatching;
    internal BatchSignal SynthesisBatch => mSynthesisBatch;
    public IReadOnlyList<SynthesisStatusSegment> GetSynthesisStatus() => mPipeline?.GetStatus() ?? [];
    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => mPipeline?.SynthesizedPitch ?? [];
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mPipeline?.SynthesizedParameters ?? EmptySynthesizedParameters;
    public IReadOnlyMap<string, SynthesizedParameter> GetEffectSynthesizedParameters(IEffect effect) => mPipeline?.GetEffectSynthesizedParameters(effect) ?? EmptySynthesizedParameters;
    public IReadOnlyList<Synthesis.SynthesizedSegment> SynthesizedSegments => mPipeline?.SynthesizedSegments ?? [];

    public MidiPart(ITrack track, MidiPartInfo info) : base(track)
    {
        Name = new(this, string.Empty);
        Pos = new(this);
        Dur = new(this);
        Gain = new(this);
        Properties = new(this);
        mVoice = new(this, new VoiceInfo());
        mNotes = new();
        mNotes.Attach(this);
        mVibratos = new(this);
        mAutomations = new(this);
        mPiecewiseAutomations = new(this);
        mEffects = new(this);
        mEffects.ItemAdded.Subscribe(OnEffectAdded);
        mEffects.ItemRemoved.Subscribe(OnEffectRemoved);
        mEffects.ListModified.Subscribe(OnEffectChainStructureModified);
        mPitchLine = new();
        mPitchLine.Attach(this);
        Dur.Modified.Subscribe(mDurationChanged);
        // 换声源：丢弃旧会话、重建新会话（context 随会话重建）。
        // 注：本订阅在一切 UI 订阅之前注册，UI 收到 Voice.Modified 时声明已刷新。
        mVoice.Modified.Subscribe(OnVoiceModified);
        // part 参数 commit：voice 的条件自动化轨集合可能随之变；重算并按需通知 UI。
        Properties.Modified.Subscribe(OnPartPropertiesModified);
        // 其余数据变更（note/pitch/automation/tempo/平移）不再由 part 驱动重分片——
        // 失效判定归插件，变更流经 SynthesisContext 转发。
        SetInfo(info);
    }

    public void InsertNote(INote note)
    {
        mNotes.Insert(note);
    }

    public bool RemoveNote(INote note)
    {
        return mNotes.Remove(note);
    }

    public IEffect CreateEffect(EffectInfo info)
    {
        return new Effect(this, info);
    }

    public void InsertEffect(int index, IEffect effect)
    {
        mEffects.Insert(index, effect);
    }

    public bool RemoveEffect(IEffect effect)
    {
        return mEffects.Remove(effect);
    }

    void OnEffectAdded(IEffect effect)
    {
        void handler() => OnEffectModified(effect);
        mEffectModifiedHandlers[effect] = handler;
        effect.Modified.Subscribe(handler);
    }

    void OnEffectRemoved(IEffect effect)
    {
        if (mEffectModifiedHandlers.Remove(effect, out var handler))
            effect.Modified.Unsubscribe(handler);
    }

    // 某个 effect 的参数/启用/自动化变化：失效与重处理由效果图各 processor 订阅自管（processor 经
    // IEffectContext 自算 dirty），宿主此处只处理条件自动化轨集合的 UI 增量刷新。
    void OnEffectModified(IEffect effect)
    {
        int index = mEffects.IndexOf(effect);
        if (index < 0)
            return;
        // 该 effect 的参数变可能改其条件自动化轨集合（仅活跃 part 才有 UI 在看，且引擎已 Init）。
        // AutomationConfigs 为 live 求值，此刻读即取当前参数值（参数值在 Notify 前已写入），无缓存时序问题。
        if (mPipeline != null && RefreshAutomationConfigsSignatureChanged())
            mAutomationConfigsModified.Invoke();
    }

    // part 参数 commit：voice 条件自动化轨集合可能随之变；重算 voice 轨集合，仅当聚合签名变时通知 UI。
    void OnPartPropertiesModified()
    {
        if (mPipeline == null)
            return;
        mVoice.RebuildAutomationConfigs(BuildPartPropertyContext());
        if (RefreshAutomationConfigsSignatureChanged())
            mAutomationConfigsModified.Invoke();
    }

    // 链结构变化（增删/重排）：各段弃处理器、从头重建效果链（voice 输出已缓存，不重跑 voice）。
    void OnEffectChainStructureModified()
    {
        mPipeline?.OnEffectChainStructureChanged();
        // 链变更经 Effects.ListModified 已驱动 UI 重建；此处只对齐签名基线，避免后续参数 commit 误判。
        mAutomationConfigsSignature = ComputeAutomationConfigsSignature();
    }

    IPartPropertyContext BuildPartPropertyContext() => new PartPropertyContext(Properties.GetInfo());

    // 聚合签名 = voice 轨集合 + 各 effect 轨集合（按 key + 字段平铺）；用于参数 commit 时的增量去抖。
    string ComputeAutomationConfigsSignature()
    {
        var sb = new StringBuilder();
        AppendAutomationConfigs(sb, "v", mVoice.AutomationConfigs);
        // 回显轨集合（context 驱动、可随参数显隐）纳入签名：其增删要驱动标题栏工具条重建。
        AppendAutomationConfigs(sb, "r", mVoice.SynthesizedParameterConfigs);
        for (int i = 0; i < mEffects.Count; i++)
            AppendAutomationConfigs(sb, "e" + i, mEffects[i].AutomationConfigs);
        return sb.ToString();
    }

    static void AppendAutomationConfigs(StringBuilder sb, string source, IReadOnlyOrderedMap<string, AutomationConfig> configs)
    {
        sb.Append(source).Append('{');
        foreach (var kvp in configs)
        {
            var c = kvp.Value;
            sb.Append(kvp.Key).Append('|').Append(c.DisplayText).Append('|')
              .Append(c.DefaultValue).Append('|').Append(c.MinValue).Append('|')
              .Append(c.MaxValue).Append('|').Append(c.Color).Append(';');
        }
        sb.Append('}');
    }

    bool RefreshAutomationConfigsSignatureChanged()
    {
        var next = ComputeAutomationConfigsSignature();
        if (next == mAutomationConfigsSignature)
            return false;
        mAutomationConfigsSignature = next;
        return true;
    }

    public void InsertVibrato(Vibrato vibrato)
    {
        int index = 0;
        for (; index < mVibratos.Count; index++)
        {
            if (mVibratos[index].Pos.Value < vibrato.Pos.Value)
                continue;

            if (mVibratos[index].Pos.Value > vibrato.Pos.Value)
                break;

            if (mVibratos[index].Dur.Value < vibrato.Dur.Value)
                break;
        }
        mVibratos.Insert(index, vibrato);
    }

    public bool RemoveVibrato(Vibrato vibrato)
    {
        return mVibratos.Remove(vibrato);
    }

    public void LockPitch(double start, double end, double extension)
    {
        double startTime = TempoManager.GetTime(Pos + start);
        double endTime = TempoManager.GetTime(Pos + end);
        foreach (var pitchLine in SynthesizedPitch)
        {
            if (pitchLine.Count < 2)
                continue;

            var pitchEndTime = pitchLine.Last().X;
            if (pitchEndTime < startTime)
                continue;

            var pitchStartTime = pitchLine.First().X;
            if (pitchStartTime > endTime)
                break;

            double pos = Pos;
            var points = new List<Point>();
            foreach (var point in pitchLine)
            {
                double time = point.X;
                if (time < startTime)
                    continue;

                if (time > endTime)
                    break;

                points.Add(new(TempoManager.GetTick(time) - pos, point.Y));
            }
            mPitchLine.AddLine(points, extension);
        }
    }

    public double[] GetVibratoDeviation(IReadOnlyList<double> ticks, string automationID = "")
    {
        // live 侧投影到共享纯函数（与冻结快照同一套算法）：振幅按目标轨现场解析。
        var vibratos = new List<VibratoMath.VibratoData>();
        foreach (var vibrato in mVibratos)
        {
            double amplitude = string.IsNullOrEmpty(automationID) ? vibrato.Amplitude : vibrato.AffectedAutomations.GetValueOrDefault(automationID, 0);
            if (amplitude == 0)
                continue;

            vibratos.Add(new VibratoMath.VibratoData(
                vibrato.Pos, vibrato.Dur, vibrato.Frequency, amplitude,
                vibrato.Phase, vibrato.Attack, vibrato.Release));
        }

        Func<double[], double[]>? envelopeSampler = Automations.TryGetValue(ConstantDefine.VibratoEnvelopeID, out var vibratoEnvelope)
            ? vibratoEnvelope.GetValues
            : null;
        return VibratoMath.GetDeviation(vibratos, ticks, envelopeSampler, Pos.Value, TempoManager.GetTime);
    }

    public double[] GetFinalPitch(IReadOnlyList<double> ticks)
    {
        var pitch = mPitchLine.GetValues(ticks);
        var vibratos = GetVibratoDeviation(ticks);
        for (int i = 0; i < ticks.Count; i++)
        {
            if (double.IsNaN(pitch[i]))
                continue;

            pitch[i] += vibratos[i];
        }

        return pitch;
    }

    public double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        double[] values;
        if (mAutomations.TryGetValue(automationID, out var automation))
        {
            values = automation.GetValues(ticks);
        }
        else
        {
            var defaultValue = GetEffectiveAutomationConfig(automationID).DefaultValue;
            values = new double[ticks.Count];
            values.Fill(defaultValue);
        }

        return values;
    }

    public double[] GetFinalAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        var values = GetAutomationValues(ticks, automationID);

        var vibratos = GetVibratoDeviation(ticks, automationID);
        for (int i = 0; i < ticks.Count; i++)
        {
            values[i] += vibratos[i];
        }

        return values;
    }

    public double[] GetBasePitchByTimes(IReadOnlyList<double> times)
    {
        double[] pitch = new double[times.Count];
        return pitch;
    }

    // 批量变更括号（undo/redo 重放时同样成对触发）：让插件把重活（如重分片）延迟到 BatchEnd。
    public void BeginMergeDirty()
    {
        PushAndDo(new Command(mSynthesisBatch.Begin, mSynthesisBatch.End));
    }

    public void EndMergeDirty()
    {
        PushAndDo(new Command(mSynthesisBatch.End, mSynthesisBatch.Begin));
    }

    public override void Activate()
    {
        RebuildSynthesisPipeline();
    }

    public override void Deactivate()
    {
        DisposeSynthesisPipeline();
    }

    void OnVoiceModified()
    {
        if (mPipeline != null)
            RebuildSynthesisPipeline();
    }

    void RebuildSynthesisPipeline()
    {
        DisposeSynthesisPipeline();
        mPipeline = new VoiceSynthesisPipeline(this, mVoice.Type, mVoice.ID);
        mPipeline.StatusChanged += OnPipelineStatusChanged;
        mVoice.RefreshDeclarations(mPipeline.Session, BuildPartPropertyContext());
        // 重置签名基线（激活 / 换源时 UI 经各自既有触发整体重建，此处不发 AutomationConfigsModified，
        // 只对齐基线供后续参数 commit 的增量比对）。
        mAutomationConfigsSignature = ComputeAutomationConfigsSignature();
        mSynthesisStatusChanged.Invoke();
    }

    void DisposeSynthesisPipeline()
    {
        if (mPipeline == null)
            return;

        mPipeline.StatusChanged -= OnPipelineStatusChanged;
        mPipeline.Dispose();
        mPipeline = null;
        mVoice.RefreshDeclarations(null, BuildPartPropertyContext());
        foreach (var note in mNotes)
        {
            note.SynthesizedPhonemes = null;
        }
    }

    void OnPipelineStatusChanged()
    {
        mSynthesisStatusChanged.Invoke();
    }

    public INote CreateNote(NoteInfo info)
    {
        return new Note(this, info);
    }

    public Vibrato CreateVibrato(VibratoInfo info)
    {
        var vibrato = new Vibrato(this);
        vibrato.SetInfo(info);
        return vibrato;
    }

    public IAutomation? AddAutomation(string automationID)
    {
        if (mAutomations.TryGetValue(automationID, out var value))
            return value;

        if (!IsEffectiveAutomation(automationID))
            return null;

        var config = GetEffectiveAutomationConfig(automationID);
        var automation = CreateAutomation(automationID, new() { DefaultValue = config.DefaultValue });
        mAutomations.Add(automationID, automation);
        return automation;
    }

    public bool IsEffectiveAutomation(string id)
    {
        return Voice.AutomationConfigs.TryGetValue(id, out var config) && !config.IsPiecewise;
    }

    public AutomationConfig GetEffectiveAutomationConfig(string id)
    {
        if (Voice.AutomationConfigs.TryGetValue(id, out var config) && !config.IsPiecewise)
            return config;

        throw new ArgumentException(string.Format("Automation {0} is not effective!", id));
    }

    // 分段轨按需创建（无默认基线，建空轨）；轨须在当前声明里才创建。孤儿轨（声明已消失但数据仍在）不重建、保留隐藏。
    public IPiecewiseAutomation? AddPiecewiseAutomation(string id)
    {
        if (mPiecewiseAutomations.TryGetValue(id, out var value))
            return value;

        if (!IsEffectivePiecewiseAutomation(id))
            return null;

        var automation = new PiecewiseAutomation();
        mPiecewiseAutomations.Add(id, automation);
        return automation;
    }

    public bool IsEffectivePiecewiseAutomation(string id)
    {
        return Voice.AutomationConfigs.TryGetValue(id, out var config) && config.IsPiecewise;
    }

    public AutomationConfig GetEffectivePiecewiseAutomationConfig(string id)
    {
        if (Voice.AutomationConfigs.TryGetValue(id, out var config) && config.IsPiecewise)
            return config;

        throw new ArgumentException(string.Format("Piecewise automation {0} is not effective!", id));
    }

    public override MidiPartInfo GetInfo()
    {
        return new()
        {
            Name = Name,
            Pos = Pos,
            Dur = Dur,
            Gain = Gain,
            Notes = mNotes.GetInfo().ToInfo(),
            Effects = mEffects.GetInfo().ToInfo(),
            Automations = mAutomations.GetInfo().ToInfo(),
            PiecewiseAutomations = mPiecewiseAutomations.PiecewiseAutomationsToInfo(),
            Pitch = mPitchLine.GetInfo(),
            Vibratos = mVibratos.GetInfo().ToInfo(),
            Voice = mVoice.GetInfo(),
            Properties = Properties.GetInfo(),
        };
    }

    public void SetInfo(MidiPartInfo info)
    {
        using var _ = MergeNotify();
        Name.SetInfo(info.Name);
        Pos.SetInfo(info.Pos);
        Dur.SetInfo(info.Dur);
        Gain.SetInfo(info.Gain);
        mNotes.SetInfo(info.Notes.Convert(CreateNote).ToArray());
        mEffects.SetInfo(info.Effects.Convert(CreateEffect).ToArray());
        mVibratos.SetInfo(info.Vibratos.Convert(CreateVibrato).ToArray());
        mAutomations.SetInfo(info.Automations.Convert(CreateAutomation).ToMap());
        mPiecewiseAutomations.SetInfo(info.PiecewiseAutomations.ToPiecewiseAutomations());
        mPitchLine.SetInfo(info.Pitch);
        mVoice.SetInfo(info.Voice);
        Properties.SetInfo(info.Properties);
    }

    // 合成失效不再由 part 驱动：automation 的 RangeModified/DefaultValue 变更经
    // SynthesisContext 转发给插件，由插件按自己的失效依赖图标脏。
    Automation CreateAutomation(string automationID, AutomationInfo info)
    {
        return new Automation(this, info);
    }

    public override IAudioData GetAudioData(int offset, int count)
    {
        float[] data = new float[count];
        int sampleRate = ((IAudioSource)this).SampleRate;
        double startTime = ((IAudioSource)this).StartTime;
        int partOffset = (int)(sampleRate * startTime);
        // 各段末级音频（已适配工程率）按绝对时间对齐混入 part 缓冲（段不重叠；若重叠则 += 叠加）。
        foreach (var segment in SynthesizedSegments)
        {
            var audio = segment.Audio;
            if (audio.Samples is not { } samples)
                continue;

            int audioOffset = (int)(audio.StartTime * audio.SampleRate);
            int startIndex = partOffset + offset - audioOffset;
            int from = Math.Max(startIndex, 0);
            int to = Math.Min(startIndex + count, samples.Length);
            for (int i = from; i < to; i++)
                data[i - startIndex] += samples[i];
        }
        double pos = Pos;
        var ticks = new double[count];
        for (int i = 0; i < count; i++)
        {
            ticks[i] = TempoManager.GetTick((double)(offset + i) / sampleRate + startTime) - pos;
        }
        var volumes = GetFinalAutomationValues(ticks, ConstantDefine.VolumeID);
        for (int i = 0; i < volumes.Length; i++)
        {
            data[i] = (float)(data[i] * Volume2Level(volumes[i]));
        }
        if (Gain != 0)
        {
            float volume = (float)MusicTheory.dB2Level(Gain);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= volume;
            }
        }
        return new MonoAudioData(data);
    }

    public override void OnSampleRateChanged()
    {
        mPipeline?.OnSampleRateChanged();
    }

    static MidiPart()
    {
        var a = Math.Log(Math.Pow(10, 1 / 20.0));
        x0 = 1 / a - 12;
        k = Math.Pow(10, x0 / 20) * a;
    }
    const double VOLUME_MAX = 12;
    public static double Volume2Level(double volume)
    {
        if (volume > x0)
            return MusicTheory.dB2Level(volume);

        if (volume > -VOLUME_MAX)
            return (volume + VOLUME_MAX) * k;

        return 0;
    }
    static readonly double x0;
    static readonly double k;

    protected override int SampleRate => AudioEngine.SampleRate.Value;

    readonly ActionEvent mSynthesisStatusChanged = new();
    readonly ActionEvent mAutomationConfigsModified = new();
    string mAutomationConfigsSignature = string.Empty;
    readonly BatchSignal mSynthesisBatch = new();
    VoiceSynthesisPipeline? mPipeline;

    readonly NoteList mNotes;
    readonly DataObjectList<Vibrato> mVibratos;
    readonly DataObjectMap<string, IAutomation> mAutomations;
    readonly DataObjectMap<string, IPiecewiseAutomation> mPiecewiseAutomations;
    readonly DataObjectList<IEffect> mEffects;
    readonly Dictionary<IEffect, Action> mEffectModifiedHandlers = new();
    readonly PiecewiseAutomation mPitchLine;
    readonly Voice mVoice;

    static readonly Map<string, SynthesizedParameter> EmptySynthesizedParameters = new();

    class NoteList : DataObjectLinkedList<INote>, INoteList
    {
        public IMergableEvent SelectionChanged => mSelectionChanged;

        public NoteList()
        {
            ListModified.Subscribe(mSelectionChanged);
            this.WhenAny(note => note.SelectionChanged).Subscribe(mSelectionChanged);
        }

        protected override bool IsInOrder(INote prev, INote next)
        {
            if (prev.StartPos() < next.StartPos())
                return true;

            if (prev.StartPos() > next.StartPos())
                return false;

            if (prev.EndPos() > next.EndPos())
                return true;

            if (prev.EndPos() < next.EndPos())
                return false;

            return true;
        }

        readonly MergableEvent mSelectionChanged = new();
    }
}
