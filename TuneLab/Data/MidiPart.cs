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
    public ISoundSource SoundSource => mSource;
    IDataProperty<double> IMidiPart.Gain => Gain;
    public IReadOnlyDataObjectMap<string, IAutomation> Automations => mAutomations;
    // 声明分段轨（除 Pitch 外、声源声明的可编辑分段曲线，即 AutomationConfig.IsPiecewise 的轨），按轨 id 存。
    // Pitch 是 part 专属常驻通道、在音符区编辑，不入此 map。
    public IReadOnlyDataObjectMap<string, IPiecewiseAutomation> PiecewiseAutomations => mPiecewiseAutomations;
    public IReadOnlyDataObjectList<IEffect> Effects => mEffects;
    public IPiecewiseAutomation Pitch => mPitchLine;
    public IReadOnlyDataObjectLinkedList<Vibrato> Vibratos => mVibratos;

    // —— 合成消费面（session 模型）：状态/产物全由插件托管，宿主经管线包装拉取展示 ——
    public ISynthesisPipeline? SynthesisPipeline => mPipeline;
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
        mSource = new(this, new SoundSourceInfo());
        mNotes = new();
        mNotes.Attach(this);
        mVibratos = new();
        mVibratos.Attach(this);
        mAutomations = new(this);
        mPiecewiseAutomations = new(this);
        mEffects = new(this);
        mEffects.ItemAdded.Subscribe(OnEffectAdded);
        mEffects.ItemRemoved.Subscribe(OnEffectRemoved);
        mEffects.MembershipModified.Subscribe(OnEffectChainMembershipModified);
        mPitchLine = new();
        mPitchLine.Attach(this);
        Dur.Modified.Subscribe(mDurationChanged);
        // 换声源：丢弃旧会话、重建新会话（context 随会话重建）。
        // 注：本订阅在一切 UI 订阅之前注册，UI 收到 Voice.Modified 时声明已刷新。
        mSource.Modified.Subscribe(OnVoiceModified);
        // part 参数 commit：voice 的条件自动化轨集合可能随之变；重算并按需通知 UI。
        Properties.Modified.Subscribe(OnPartPropertiesModified);
        // 其余数据变更（note/pitch/automation/tempo/平移）不再由 part 驱动重分片——
        // 失效判定归插件，变更流经 VoiceSynthesisContext 转发。
        SetInfo(info);
    }

    public void InsertNote(INote note)
    {
        mNotes.Insert(note);
    }

    public bool RemoveNote(INote note)
    {
        // 归属由集合判定（链表反向指针）；非成员删除是编程错误，DEBUG 期就地暴露，Release 仍宽容 no-op。
        System.Diagnostics.Debug.Assert(mNotes.Contains(note), "RemoveNote: note 不属于本 part。");
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
        mSource.RebuildAutomationConfigs(BuildPartPropertyContext());
        if (RefreshAutomationConfigsSignatureChanged())
            mAutomationConfigsModified.Invoke();
    }

    // 链结构变化（增删/重排）：各段弃处理器、从头重建效果链（voice 输出已缓存，不重跑 voice）。
    void OnEffectChainMembershipModified()
    {
        mPipeline?.OnEffectChainStructureChanged();
        // 链变更经 Effects.MembershipModified 已驱动 UI 重建；此处只对齐签名基线，避免后续参数 commit 误判。
        mAutomationConfigsSignature = ComputeAutomationConfigsSignature();
    }

    PartPropertyContext BuildPartPropertyContext() => PartPropertyContext.Single(this);

    // 聚合签名 = voice 轨集合 + 各 effect 轨集合（按 key + 字段平铺）；用于参数 commit 时的增量去抖。
    string ComputeAutomationConfigsSignature()
    {
        var sb = new StringBuilder();
        AppendAutomationConfigs(sb, "v", mSource.AutomationConfigs);
        // 回显轨集合（context 驱动、可随参数显隐）纳入签名：其增删要驱动标题栏工具条重建。
        AppendAutomationConfigs(sb, "r", mSource.SynthesizedParameterConfigs);
        for (int i = 0; i < mEffects.Count; i++)
            AppendAutomationConfigs(sb, "e" + i, mEffects[i].AutomationConfigs);
        return sb.ToString();
    }

    static void AppendAutomationConfigs(StringBuilder sb, string source, IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs)
    {
        sb.Append(source).Append('{');
        foreach (var kvp in configs)
        {
            var c = kvp.Value;
            sb.Append(kvp.Key.Id).Append('|').Append(kvp.Key.DisplayText).Append('|')
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
        // 链表按 VibratoList.IsInOrder（Pos↑、同 Pos 时 Dur↓）自排，无需手工定位下标。
        mVibratos.Insert(vibrato);
    }

    public bool RemoveVibrato(Vibrato vibrato)
    {
        System.Diagnostics.Debug.Assert(mVibratos.Contains(vibrato), "RemoveVibrato: vibrato 不属于本 part。");
        return mVibratos.Remove(vibrato);
    }

    // 改 note 排序键（pos/dur）统一走 move：摘除→跑 mutate→按新键重插，维序与 undo 由集合保证。
    public void MoveNote(INote note, Action mutate) => mNotes.Move(note, mutate);
    public void MoveNotes(IReadOnlyCollection<INote> notes, Action mutate) => mNotes.Move(notes, mutate);
    public void MoveVibrato(Vibrato vibrato, Action mutate) => mVibratos.Move(vibrato, mutate);
    public void MoveVibratos(IReadOnlyCollection<Vibrato> vibratos, Action mutate) => mVibratos.Move(vibratos, mutate);

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

    // 音符基线 pitch：每个 tick 取覆盖它的音符半音值，无音符为 NaN。ticks part 相对、升序。
    // 重叠时"后 note 头盖前 note 尾"：按起点升序入栈，栈顶=最近开始且仍覆盖的 note；
    // 栈顶结束则弹出回退到下层 note（其仍覆盖则恢复其音高）。
    public double[] GetBasePitch(IReadOnlyList<double> ticks)
    {
        double[] pitch = new double[ticks.Count];
        pitch.Fill(double.NaN);
        if (ticks.Count == 0)
            return pitch;

        var stack = new List<INote>();
        using var it = mNotes.GetEnumerator();
        INote? next = it.MoveNext() ? it.Current : null;
        for (int i = 0; i < ticks.Count; i++)
        {
            double tick = ticks[i];
            while (next != null && next.StartPos() <= tick)
            {
                stack.Add(next);
                next = it.MoveNext() ? it.Current : null;
            }
            while (stack.Count > 0 && stack[^1].EndPos() <= tick)
                stack.RemoveAt(stack.Count - 1);

            if (stack.Count > 0)
                pitch[i] = stack[^1].Pitch.Value;
        }
        return pitch;
    }

    // 有效基线（单点）：用户绘制曲线优先，未绘制处落到覆盖音符的半音；都没有则 NaN。
    // 颤音控制柄定位用它，使没画 pitch 时也能锚在音符上、即时可编辑。重叠同样"后 note 盖前 note"（取最末覆盖者）。
    public double GetEffectivePitchValue(double tick)
    {
        double drawn = mPitchLine.GetValue(tick);
        if (!double.IsNaN(drawn))
            return drawn;

        double result = double.NaN;
        foreach (var note in mNotes)
        {
            if (note.StartPos() > tick)
                break;

            if (note.EndPos() > tick)
                result = note.Pitch.Value;
        }
        return result;
    }

    // 颤音覆盖区内、未绘制 pitch 处的兜底虚线波（= 音符基线 + 颤音偏差）：只在"画了颤音的位置"显示预期音高，
    // 颤音区外不画。已绘制 pitch 的区段交给实线 GetFinalPitch，避免二次叠加。
    public double[] GetVibratoFallbackPitch(IReadOnlyList<double> ticks)
    {
        double[] result = new double[ticks.Count];
        result.Fill(double.NaN);
        if (mVibratos.Count == 0)
            return result;

        double[] drawn = mPitchLine.GetValues(ticks);
        double[] basePitch = GetBasePitch(ticks);
        double[] deviation = GetVibratoDeviation(ticks);
        using var it = mVibratos.GetEnumerator();
        Vibrato? vibrato = it.MoveNext() ? it.Current : null;
        for (int i = 0; i < ticks.Count; i++)
        {
            double tick = ticks[i];
            while (vibrato != null && vibrato.EndPos() < tick)
                vibrato = it.MoveNext() ? it.Current : null;
            if (vibrato == null)
                break;

            if (vibrato.StartPos() > tick || !double.IsNaN(drawn[i]) || double.IsNaN(basePitch[i]))
                continue;

            result[i] = basePitch[i] + deviation[i];
        }
        return result;
    }

    // 悬浮添加预览波：在有效基线（绘制优先、否则音符）上叠加单个待建颤音的偏移，无基线处 NaN。
    public double[] GetVibratoAddPreviewPitch(IReadOnlyList<double> ticks, VibratoInfo info)
    {
        double[] drawn = mPitchLine.GetValues(ticks);
        double[] basePitch = GetBasePitch(ticks);
        var data = new[] { new VibratoMath.VibratoData(
            info.Pos, info.Dur, info.Frequency, info.Amplitude, info.Phase, info.Attack, info.Release) };
        Func<double[], double[]>? envelopeSampler = Automations.TryGetValue(ConstantDefine.VibratoEnvelopeID, out var envelope)
            ? envelope.GetValues
            : null;
        double[] deviation = VibratoMath.GetDeviation(data, ticks, envelopeSampler, Pos.Value, TempoManager.GetTime);
        double[] result = new double[ticks.Count];
        for (int i = 0; i < ticks.Count; i++)
        {
            double baseline = double.IsNaN(drawn[i]) ? basePitch[i] : drawn[i];
            result[i] = double.IsNaN(baseline) ? double.NaN : baseline + deviation[i];
        }
        return result;
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
        // 【顺序不变量，勿调换】先填声明、后建会话：mSource.AutomationConfigs 是物化缓存（详见 Voice.AutomationConfigs），
        // 必须在建会话之前填好——否则会话构造期经 context.Automations 订阅自己声明的轨会读到空缓存、订阅落空、绘制参数不重渲。
        mSource.RefreshDeclarations(BuildPartPropertyContext());
        if (mSource.Kind == SourceKind.Voice)
        {
            var voicePipeline = new VoiceSynthesisPipeline(this, mSource.Type, mSource.ID);
            voicePipeline.StatusChanged += OnPipelineStatusChanged;
            mSource.SetSession(voicePipeline.Session);   // 注入会话供 DefaultLyric 等运行时取值（建会话之后）
            mPipeline = voicePipeline;
        }
        else
        {
            // instrument 无会话级 DefaultLyric 注入（恒 "a"）；产物仅音频 + 参数回显，无音素回填。
            var instrumentPipeline = new InstrumentSynthesisPipeline(this, mSource.Type, mSource.ID);
            instrumentPipeline.StatusChanged += OnPipelineStatusChanged;
            mPipeline = instrumentPipeline;
        }
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
        mSource.SetSession(null);
        mSource.RefreshDeclarations(BuildPartPropertyContext());
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
        return SoundSource.AutomationConfigs.TryGetValue(id, out var config) && !config.IsPiecewise;
    }

    public AutomationConfig GetEffectiveAutomationConfig(string id)
    {
        if (SoundSource.AutomationConfigs.TryGetValue(id, out var config) && !config.IsPiecewise)
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
        return SoundSource.AutomationConfigs.TryGetValue(id, out var config) && config.IsPiecewise;
    }

    public AutomationConfig GetEffectivePiecewiseAutomationConfig(string id)
    {
        if (SoundSource.AutomationConfigs.TryGetValue(id, out var config) && config.IsPiecewise)
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
            SoundSource = mSource.GetInfo(),
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
        mSource.SetInfo(info.SoundSource);
        Properties.SetInfo(info.Properties);
    }

    // 合成失效不再由 part 驱动：automation 的 RangeModified/DefaultValue 变更经
    // VoiceSynthesisContext 转发给插件，由插件按自己的失效依赖图标脏。
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
    ISynthesisPipeline? mPipeline;

    readonly NoteList mNotes;
    readonly VibratoList mVibratos;
    readonly DataObjectMap<string, IAutomation> mAutomations;
    readonly DataObjectMap<string, IPiecewiseAutomation> mPiecewiseAutomations;
    readonly DataObjectList<IEffect> mEffects;
    readonly Dictionary<IEffect, Action> mEffectModifiedHandlers = new();
    readonly PiecewiseAutomation mPitchLine;
    readonly SoundSource mSource;

    static readonly Map<string, SynthesizedParameter> EmptySynthesizedParameters = new();

    class NoteList : SortedDataObjectLinkedList<INote>, INoteList
    {
        public IMergableEvent SelectionChanged => mSelectionChanged;

        public NoteList() : base(IsInOrder)
        {
            MembershipModified.Subscribe(mSelectionChanged);
            this.WhenAny(note => note.SelectionChanged).Subscribe(mSelectionChanged);
        }

        static bool IsInOrder(INote prev, INote next)
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

    // 颤音有序链表：与 NoteList 同构的排序键（StartPos↑、同起点时 EndPos↓，即 Pos↑/Dur↓），
    // 与改链表前 InsertVibrato 的手工定位顺序一致。
    class VibratoList : SortedDataObjectLinkedList<Vibrato>
    {
        public VibratoList() : base(IsInOrder) { }

        static bool IsInOrder(Vibrato prev, Vibrato next)
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
    }
}
