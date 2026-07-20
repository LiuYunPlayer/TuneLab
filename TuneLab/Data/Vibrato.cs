using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Utils;
using TuneLab.SDK;

namespace TuneLab.Data;

internal class Vibrato : DataObject, IDataObject<VibratoInfo>, ISelectable, ILinkedNode<Vibrato>
{
    public IActionEvent<double, double> RangeModified => mRangeModified;
    // effect 影响表（AffectedEffectAutomations）的变更区间事件：与 RangeModified 分源——改 effect 关联振幅
    // 只惊动 effect 链，不把 voice（音高偏差）标脏重合成；几何/参数/voice 影响表仍走 RangeModified
    //（voice 与 effect 都消费：几何变化同样改变 effect 轨的偏移区间）。
    public IActionEvent<double, double> EffectAmplitudesModified => mEffectAmplitudesModified;
    public IActionEvent SelectionChanged => mSelectionChanged;
    public IMidiPart Part => mPart;

    Vibrato? ILinkedNode<Vibrato>.Next { get; set; } = null;
    Vibrato? ILinkedNode<Vibrato>.Previous { get; set; } = null;
    ILinkedList<Vibrato>? ILinkedNode<Vibrato>.LinkedList { get; set; }
    public bool IsSelected { get => mIsSelected; set { if (mIsSelected == value) return; mIsSelected = value; mSelectionChanged.Invoke(); } }
    public DataStruct<double> Pos { get; } = new();
    public DataStruct<double> Dur { get; } = new();
    public DataStruct<double> Frequency { get; } = new();
    public DataStruct<double> Amplitude { get; } = new();
    public DataStruct<double> Phase { get; } = new();
    public DataStruct<double> Attack { get; } = new();
    public DataStruct<double> Release { get; } = new();
    public DataMap<string, double> AffectedAutomations { get; } = new();
    // 对 effect 自动化轨的影响振幅（与 voice 表平行、命名空间互不相扰）：键 = effect 实例稳定 id + 轨 id。
    // 锚实例身份而非链内位置：重排/替换无需重映射；删除留孤儿条目（不裁剪，与孤儿曲线同判例），
    // undo 恢复同 id 的 effect 即自然重连。
    public DataMap<EffectAutomationRef, double> AffectedEffectAutomations { get; } = new();

    public Vibrato(IMidiPart part)
    {
        mPart = part;
        mMergeHandler = new(NotifyRangeModified);
        Pos.Attach(this);
        Dur.Attach(this);
        Frequency.Attach(this);
        Amplitude.Attach(this);
        Phase.Attach(this);
        Attack.Attach(this);
        Release.Attach(this);
        AffectedAutomations.Attach(this);
        AffectedEffectAutomations.Attach(this);
        // 变更源二分：子级 settled 订阅只置源标记（通知显式沿父链上爬 + merge 收口先下行补发子级欠账，
        // 故子标记必先于本结点 settled），聚合 Modified（settled、merge 收口后才发）按标记分发到两路事件——
        // 保持「结果态才通知」的语义与事件基数不变。
        Pos.Modified.Subscribe(MarkVoiceSource);
        Dur.Modified.Subscribe(MarkVoiceSource);
        Frequency.Modified.Subscribe(MarkVoiceSource);
        Amplitude.Modified.Subscribe(MarkVoiceSource);
        Phase.Modified.Subscribe(MarkVoiceSource);
        Attack.Modified.Subscribe(MarkVoiceSource);
        Release.Modified.Subscribe(MarkVoiceSource);
        AffectedAutomations.Modified.Subscribe(MarkVoiceSource);
        AffectedEffectAutomations.Modified.Subscribe(MarkEffectSource);
        Modified.Subscribe(DispatchRangeModified);
    }

    void MarkVoiceSource() => mVoiceSourceDirty = true;
    void MarkEffectSource() => mEffectSourceDirty = true;

    void DispatchRangeModified()
    {
        if (mVoiceSourceDirty)
        {
            mVoiceSourceDirty = false;
            mMergeHandler.Trigger();
        }
        if (mEffectSourceDirty)
        {
            mEffectSourceDirty = false;
            NotifyEffectAmplitudesModified();
        }
    }

    // —— 影响表的统一口径（按 AutomationKey 分派 voice / effect 表；lane 不参与颤音，一律视作无关联）。
    //    AutomationKey 携槽位下标（UI 路由键），此处经本 part 现场解析成实例 id 再查/写表；
    //    槽位越界（交互瞬间链已变）按无关联处理。——

    EffectAutomationRef? ResolveEffectRef(AutomationKey key)
    {
        if (key.EffectIndex < 0 || key.EffectIndex >= mPart.Effects.Count)
            return null;
        return new EffectAutomationRef(mPart.Effects[key.EffectIndex].Id, key.Id);
    }

    public double GetAmplitude(AutomationKey key)
    {
        if (key.IsLane)
            return 0;
        if (!key.IsEffect)
            return AffectedAutomations.GetValueOrDefault(key.Id, 0);
        return ResolveEffectRef(key) is { } reference ? AffectedEffectAutomations.GetValueOrDefault(reference, 0) : 0;
    }

    public bool IsAssociated(AutomationKey key)
    {
        if (key.IsLane)
            return false;
        if (!key.IsEffect)
            return AffectedAutomations.ContainsKey(key.Id);
        return ResolveEffectRef(key) is { } reference && AffectedEffectAutomations.ContainsKey(reference);
    }

    // 写振幅（无关联即建立关联）。
    public void SetAmplitude(AutomationKey key, double amplitude)
    {
        System.Diagnostics.Debug.Assert(!key.IsLane);
        if (key.IsEffect)
        {
            if (ResolveEffectRef(key) is not { } reference)
                return;
            if (AffectedEffectAutomations.ContainsKey(reference))
                AffectedEffectAutomations[reference] = amplitude;
            else
                AffectedEffectAutomations.Add(reference, amplitude);
        }
        else
        {
            if (AffectedAutomations.ContainsKey(key.Id))
                AffectedAutomations[key.Id] = amplitude;
            else
                AffectedAutomations.Add(key.Id, amplitude);
        }
    }

    public void RemoveAssociation(AutomationKey key)
    {
        if (key.IsLane)
            return;
        if (key.IsEffect)
        {
            if (ResolveEffectRef(key) is { } reference)
                AffectedEffectAutomations.Remove(reference);
        }
        else
        {
            AffectedAutomations.Remove(key.Id);
        }
    }

    public void BeginRangeModify()
    {
        if (!mMergeHandler.IsMerging)
        {
            NotifyRangeModified();
        }

        mMergeHandler.Begin();
    }

    public void EndRangeModify()
    {
        mMergeHandler.End();
    }

    public VibratoInfo GetInfo()
    {
        return new VibratoInfo()
        {
            Pos = Pos,
            Dur = Dur,
            Frequency = Frequency,
            Amplitude = Amplitude,
            Phase = Phase,
            Attack = Attack,
            Release = Release,
            AffectedAutomations = AffectedAutomations.GetInfo(),
            AffectedEffectAutomations = ToInfo(AffectedEffectAutomations)
        };
    }

    // 运行期扁平表（键 = 实例 id + 轨 id）↔ info 嵌套表（实例 id → 轨 id → 振幅）。
    static Map<string, Map<string, double>> ToInfo(IReadOnlyMap<EffectAutomationRef, double> map)
    {
        var info = new Map<string, Map<string, double>>();
        foreach (var kvp in map)
        {
            if (!info.TryGetValue(kvp.Key.EffectId, out var tracks))
            {
                tracks = new Map<string, double>();
                info.Add(kvp.Key.EffectId, tracks);
            }
            tracks.Add(kvp.Key.Id, kvp.Value);
        }
        return info;
    }

    static Map<EffectAutomationRef, double> FromInfo(IReadOnlyMap<string, Map<string, double>> info)
    {
        var map = new Map<EffectAutomationRef, double>();
        foreach (var slot in info)
            foreach (var track in slot.Value)
                map.Add(new EffectAutomationRef(slot.Key, track.Key), track.Value);
        return map;
    }

    public void SetInfo(VibratoInfo info)
    {
        using var _ = MergeNotify();
        Pos.SetInfo(info.Pos);
        Dur.SetInfo(info.Dur);
        Frequency.SetInfo(info.Frequency);
        Amplitude.SetInfo(info.Amplitude);
        Phase.SetInfo(info.Phase);
        Attack.SetInfo(info.Attack);
        Release.SetInfo(info.Release);
        AffectedAutomations.SetInfo(info.AffectedAutomations);
        AffectedEffectAutomations.SetInfo(FromInfo(info.AffectedEffectAutomations));
    }

    void NotifyRangeModified()
    {
        mRangeModified.Invoke(this.StartPos(), this.EndPos());
    }

    void NotifyEffectAmplitudesModified()
    {
        mEffectAmplitudesModified.Invoke(this.StartPos(), this.EndPos());
    }

    bool mIsSelected = false;
    bool mVoiceSourceDirty = false;
    bool mEffectSourceDirty = false;

    readonly MergeHandler mMergeHandler;
    readonly ActionEvent<double, double> mRangeModified = new();
    readonly ActionEvent<double, double> mEffectAmplitudesModified = new();
    readonly ActionEvent mSelectionChanged = new();
    readonly IMidiPart mPart;
}

internal static class VibratoExtension
{
    public static double StartPos(this Vibrato vibrato)
    {
        return vibrato.Pos;
    }

    public static double EndPos(this Vibrato vibrato)
    {
        return vibrato.Pos + vibrato.Dur;
    }

    public static double GlobalStartPos(this Vibrato vibrato)
    {
        return vibrato.Part.Pos.Value + vibrato.StartPos();
    }

    public static double GlobalEndPos(this Vibrato vibrato)
    {
        return vibrato.Part.Pos.Value + vibrato.EndPos();
    }

    public static double GlobalStartTime(this Vibrato vibrato)
    {
        return vibrato.Part.TempoManager.GetTime(vibrato.GlobalStartPos());
    }

    public static double GlobalEndTime(this Vibrato vibrato)
    {
        return vibrato.Part.TempoManager.GetTime(vibrato.GlobalEndPos());
    }

    public static double GlobalAttackTime(this Vibrato vibrato)
    {
        return vibrato.GlobalStartTime() + vibrato.Attack;
    }

    public static double GlobalReleaseTime(this Vibrato vibrato)
    {
        return vibrato.GlobalEndTime() - vibrato.Release;
    }

    public static double GlobalAttackTick(this Vibrato vibrato)
    {
        return vibrato.Part.TempoManager.GetTick(vibrato.GlobalAttackTime());
    }

    public static double GlobalReleaseTick(this Vibrato vibrato)
    {
        return vibrato.Part.TempoManager.GetTick(vibrato.GlobalReleaseTime());
    }
}
