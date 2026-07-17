using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using TuneLab.Animation;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.Configs;

namespace TuneLab.UI;

internal partial class AutomationRenderer
{
    protected override void OnScroll(WheelEventArgs e)
    {
        switch (e.KeyModifiers)
        {
            case ModifierKeys.Shift:
                TickAxis.AnimateMove(240 * e.Delta.Y);
                break;
            case ModifierKeys.Ctrl:
                TickAxis.AnimateScale(TickAxis.Coor2Pos(e.Position.X), e.Delta.Y);
                break;
            default:
                break;
        }
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        switch (mState)
        {
            case State.None:
                bool ctrl = (e.KeyModifiers & ModifierKeys.Ctrl) != 0;
                bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
                bool shift = (e.KeyModifiers & ModifierKeys.Shift) != 0;
                var item = ItemAt(e.Position);

                // Shift+主键拖 = 画范围选区（与音符区共用同一条 tick 带，零工具切换）。先于工具/锚点/绘制逻辑拦截。
                // 未拖(点击)的清空交给 OnMouseUp 的点击阈值统一判定（mPrimaryDownPos 记于此）。
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    mPrimaryDownPos = e.Position;
                    if (shift)
                    {
                        if (Part != null)
                            mRegionSelectionOperation.Down(e.Position.X, alt);
                        break;
                    }
                }

                // 右键 → 弹范围选区菜单：有激活选区 或 按住 Shift（同音符区；Shift 让"复制后清了区仍可粘贴"）。优先于工具右键。
                if (e.MouseButtonType == MouseButtonType.SecondaryButton && (mDependency.PianoScrollView.HasRegionSelection || shift))
                {
                    mDependency.PianoScrollView.OpenRegionMenu(e.Position.X);
                    break;
                }

                if (mDependency.PianoTool.Value == PianoTool.Anchor)
                {
                    Part?.Pitch.DeselectAllAnchors();
                    mDependency.PianoScrollView.InvalidateVisual();
                    if (ActiveIsPiecewise())
                    {
                        OnPiecewiseAnchorMouseDown(e, ctrl, item);
                        break;
                    }
                    switch (e.MouseButtonType)
                    {
                        case MouseButtonType.PrimaryButton:
                            if (item is AutomationAnchorItem anchorItem)
                            {
                                mAnchorMoveOperation.Down(e.Position, ctrl, anchorItem.Automation, anchorItem.AnchorPoint, anchorItem.MinValue, anchorItem.MaxValue);
                            }
                            else if (e.IsDoubleClick)
                            {
                                if (!TryGetActiveAutomation(out var automation, out var config, true))
                                    break;

                                var anchor = new AnchorPoint(TickAxis.X2Tick(e.Position.X) - automation.Part.Pos.Value, YToValue(e.Position.Y, config.MinValue, config.MaxValue)) { IsSelected = true };
                                automation.InsertPoint(anchor);
                                var insertedAnchor = automation.Points.FirstOrDefault(point => point.Pos == anchor.Pos);
                                if (insertedAnchor == null)
                                    break;

                                automation.Points.DeselectAllItems();
                                insertedAnchor.Select();
                                mAnchorMoveOperation.Down(e.Position, ctrl, automation, insertedAnchor, config.MinValue, config.MaxValue, true);
                            }
                            else
                            {
                                mAnchorSelectOperation.Down(e.Position, ctrl);
                            }
                            break;
                        case MouseButtonType.SecondaryButton:
                            if (item is AutomationAnchorItem deleteAnchorItem)
                            {
                                deleteAnchorItem.Automation.Points.DeselectAllItems();
                                deleteAnchorItem.Automation.DeletePoints([deleteAnchorItem.AnchorPoint]);
                            }
                            mAnchorDeleteOperation.Down(e.Position.X);
                            break;
                        default:
                            break;
                    }
                    break;
                }

                if (item is VibratoItem vibratoItem)
                {
                    switch (e.MouseButtonType)
                    {
                        case MouseButtonType.PrimaryButton:
                            if (e.IsDoubleClick)
                            {
                                if (Part == null)
                                    break;

                                var automationKey = mDependency.ActiveAutomation;
                                // 双击解除关联：voice / effect 连续轨皆可（lane / 分段轨无关联概念）。
                                if (automationKey == null || !Part.IsEffectiveAutomation(automationKey.Value))
                                    break;

                                foreach (var vibrato in Part.Vibratos.AllSelectedItems())
                                {
                                    vibrato.RemoveAssociation(automationKey.Value);
                                }
                                Part.Commit();
                            }
                            else
                            {
                                mVibratoAmplitudeOperation.Down(e.Position.Y, vibratoItem.Vibrato);
                            }
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    switch (e.MouseButtonType)
                    {
                        case MouseButtonType.PrimaryButton:
                            // Ctrl = 定值绘制：锁住按下时的 y 画水平线（保持参数值不变的重要画法）。
                            if (ActiveIsNoteLane())
                                mNoteLaneDrawOperation.Down(e.Position, ctrl);
                            else if (ActiveIsPhonemeLane())
                                mPhonemeLaneDrawOperation.Down(e.Position, ctrl);
                            else if (ActiveIsPiecewise())
                                mPiecewiseDrawOperation.Down(e.Position, ctrl);
                            else
                                mDrawOperation.Down(e.Position, ctrl);
                            break;
                        case MouseButtonType.SecondaryButton:
                            if (ActiveIsNoteLane())
                                mNoteLaneClearOperation.Down(e.Position.X);
                            else if (ActiveIsPhonemeLane())
                                mPhonemeLaneClearOperation.Down(e.Position.X);
                            else if (ActiveIsPiecewise())
                                mPiecewiseClearOperation.Down(e.Position.X);
                            else
                                mClearOperation.Down(e.Position.X);
                            break;
                        default:
                            break;
                    }
                }
                break;
            default:
                break;
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Down(e.Position.X);
    }

    protected override void OnMouseAbsoluteMove(MouseMoveEventArgs e)
    {
        if (mMiddleDragOperation.IsOperating)
            mMiddleDragOperation.Move(e.Position.X);
    }

    protected override void OnMouseRelativeMoveToView(MouseMoveEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & ModifierKeys.Ctrl) != 0;
        bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
        bool shift = (e.KeyModifiers & ModifierKeys.Shift) != 0;
        switch (mState)
        {
            case State.RegionSelecting:
                mRegionSelectionOperation.Move(e.Position.X, alt);
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam);
                break;
            case State.Drawing:
                if (mDrawOperation.IsOperating) mDrawOperation.Move(e.Position, ctrl);
                else mPiecewiseDrawOperation.Move(e.Position, ctrl);
                break;
            case State.Clearing:
                if (mClearOperation.IsOperating) mClearOperation.Move(e.Position.X);
                else mPiecewiseClearOperation.Move(e.Position.X);
                break;
            case State.AnchorSelecting:
                if (mAnchorSelectOperation.IsOperating) mAnchorSelectOperation.Move(e.Position);
                else mPiecewiseAnchorSelectOperation.Move(e.Position);
                break;
            case State.AnchorDeleting:
                if (mAnchorDeleteOperation.IsOperating) mAnchorDeleteOperation.Move(e.Position.X);
                else mPiecewiseAnchorDeleteOperation.Move(e.Position.X);
                break;
            case State.AnchorMoving:
                if (mPiecewiseAnchorMoveOperation.IsOperating) mPiecewiseAnchorMoveOperation.Move(e.Position);
                else mAnchorMoveOperation.Move(e.Position);
                break;
            case State.VibratoAmplitudeAdjusting:
                mVibratoAmplitudeOperation.Move(e.Position.Y);
                break;
            case State.NoteLaneDrawing:
                if (mNoteLaneDrawOperation.IsOperating) mNoteLaneDrawOperation.Move(e.Position, ctrl);
                else mPhonemeLaneDrawOperation.Move(e.Position, ctrl);
                break;
            case State.NoteLaneClearing:
                if (mNoteLaneClearOperation.IsOperating) mNoteLaneClearOperation.Move(e.Position.X);
                else mPhonemeLaneClearOperation.Move(e.Position.X);
                break;
            default:
                if (shift)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam);   // Shift = 画范围选区模式
                    break;
                }
                var item = ItemAt(e.Position);
                if (item is VibratoItem)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth);
                }
                else if (item is AutomationAnchorItem or PiecewiseAnchorItem)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll);
                }
                else
                {
                    Cursor = null;
                }
                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        switch (mState)
        {
            case State.RegionSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mRegionSelectionOperation.Up();
                break;
            case State.Drawing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    if (mDrawOperation.IsOperating) mDrawOperation.Up();
                    else mPiecewiseDrawOperation.Up();
                }
                break;
            case State.Clearing:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                {
                    if (mClearOperation.IsOperating) mClearOperation.Up();
                    else mPiecewiseClearOperation.Up();
                }
                break;
            case State.AnchorSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    if (mAnchorSelectOperation.IsOperating) mAnchorSelectOperation.Up();
                    else mPiecewiseAnchorSelectOperation.Up();
                }
                break;
            case State.AnchorDeleting:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                {
                    if (mAnchorDeleteOperation.IsOperating) mAnchorDeleteOperation.Up();
                    else mPiecewiseAnchorDeleteOperation.Up();
                }
                break;
            case State.AnchorMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    if (mPiecewiseAnchorMoveOperation.IsOperating) mPiecewiseAnchorMoveOperation.Up();
                    else mAnchorMoveOperation.Up();
                }
                break;
            case State.VibratoAmplitudeAdjusting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoAmplitudeOperation.Up();
                break;
            case State.NoteLaneDrawing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    if (mNoteLaneDrawOperation.IsOperating) mNoteLaneDrawOperation.Up();
                    else mPhonemeLaneDrawOperation.Up();
                }
                break;
            case State.NoteLaneClearing:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                {
                    if (mNoteLaneClearOperation.IsOperating) mNoteLaneClearOperation.Up();
                    else mPhonemeLaneClearOperation.Up();
                }
                break;
            default:
                break;
        }

        // 主键点击(未拖，位移 ≤ 阈值)即清空范围选区。真拖(画区/绘制/选锚)不触发；右键不参与。镜像音符区。
        if (e.MouseButtonType == MouseButtonType.PrimaryButton)
        {
            var d = e.Position - mPrimaryDownPos;
            if (d.X * d.X + d.Y * d.Y <= ClickThreshold * ClickThreshold)
                mDependency.PianoScrollView.ClearRegionSelection();
        }

        if (mMiddleDragOperation.IsOperating)
            mMiddleDragOperation.Up();
    }

    protected override void OnMouseEnter(MouseEnterEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseLeaveEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void UpdateItems(IItemCollection items)
    {
        if (Part == null)
            return;

        double startPos = TickAxis.MinVisibleTick;
        double endPos = TickAxis.MaxVisibleTick;

        switch (mDependency.PianoTool.Value)
        {
            case PianoTool.Vibrato:
                var vibratoActive = mDependency.ActiveAutomation;
                if (vibratoActive == AutomationKey.Voice(ConstantDefine.VibratoEnvelopeID))
                    break;

                foreach (var vibrato in Part.Vibratos)
                {
                    if (vibrato.GlobalEndPos() < startPos)
                        continue;

                    if (vibrato.GlobalStartPos() > endPos)
                        break;

                    items.Add(new VibratoItem(this) { Vibrato = vibrato });
                }
                break;
            case PianoTool.Anchor:
                var activeAutomation = mDependency.ActiveAutomation;
                if (activeAutomation == null)
                    break;

                if (ActiveIsPiecewise())
                {
                    var piecewise = Part.GetEffectivePiecewiseAutomation(activeAutomation.Value);
                    if (piecewise == null)
                        break;

                    var pconfig = Part.GetEffectivePiecewiseAutomationConfig(activeAutomation.Value);
                    var pcolor = Color.Parse(pconfig.Color);
                    foreach (var group in piecewise.AnchorGroups)
                    {
                        foreach (var point in group)
                        {
                            double t = Part.Pos.Value + point.Pos;
                            if (t < startPos || t > endPos)
                                continue;

                            items.Add(new PiecewiseAnchorItem(this)
                            {
                                Automation = piecewise,
                                AnchorPoint = point,
                                MinValue = pconfig.MinValue,
                                MaxValue = pconfig.MaxValue,
                                Color = pcolor,
                            });
                        }
                    }
                    break;
                }

                var automation = Part.GetEffectiveAutomation(activeAutomation.Value);
                if (automation == null)
                    break;

                var config = Part.GetEffectiveAutomationConfig(activeAutomation.Value);
                var color = Color.Parse(config.Color);
                foreach (var point in automation.Points)
                {
                    double tick = Part.Pos.Value + point.Pos;
                    if (tick < startPos)
                        continue;

                    if (tick > endPos)
                        break;

                    items.Add(new AutomationAnchorItem(this)
                    {
                        Automation = automation,
                        AnchorPoint = point,
                        MinValue = config.MinValue,
                        MaxValue = config.MaxValue,
                        Color = color,
                    });
                }
                break;
            default:
                break;
        }
    }

    bool TryGetActiveAutomation([NotNullWhen(true)] out IAutomation? automation, [NotNullWhen(true)] out AutomationConfig? config, bool createIfMissing)
    {
        automation = null;
        config = null;
        if (Part == null)
            return false;

        var automationKey = mDependency.ActiveAutomation;
        if (automationKey == null || !Part.IsEffectiveAutomation(automationKey.Value))
            return false;

        config = Part.GetEffectiveAutomationConfig(automationKey.Value);
        automation = Part.GetEffectiveAutomation(automationKey.Value);
        if (automation == null && createIfMissing)
        {
            automation = Part.AddEffectiveAutomation(automationKey.Value);
        }

        return automation != null;
    }

    class Operation(AutomationRenderer automationRenderer)
    {
        public AutomationRenderer AutomationRenderer => automationRenderer;
        public State State { get => AutomationRenderer.mState; set => AutomationRenderer.mState = value; }
    }

    bool ActiveIsNoteLane()
    {
        var key = mDependency.ActiveAutomation;
        return Part != null && key != null && Part.IsEffectiveNoteLane(key.Value);
    }

    bool ActiveIsPhonemeLane()
    {
        var key = mDependency.ActiveAutomation;
        return Part != null && key != null && Part.IsEffectivePhonemeLane(key.Value);
    }

    // note lane 绘制：主键拖动把扫过的 note 的属性值写为鼠标处值（Ctrl = 锁定按下值，横扫即批量定值）。
    // 与连续轨 DrawOperation 同用「DiscardTo 重放」模式：每帧丢弃未提交命令、按各 note 最新目标值整批重写
    //（同一 note 只留最后一次赋值，命令数有界），抬起统一 Commit——整段拖动一个撤销步。
    // 值写 note.Properties（数据在 note 上、非时间曲线）；命中区间与渲染共用 NoteLaneRanges（去重叠口径）。
    class NoteLaneDrawOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        // 与 phoneme lane 操作共用 State.NoteLaneDrawing，靠 mPart 区分是否本操作在跑（镜像连续/分段轨的判别方式）。
        public bool IsOperating => State == State.NoteLaneDrawing && mPart != null;

        public void Down(Avalonia.Point position, bool constantValue)
        {
            if (State != State.None)
                return;

            var part = AutomationRenderer.Part;
            var key = AutomationRenderer.mDependency.ActiveAutomation;
            if (part == null || key == null || !part.IsEffectiveNoteLane(key.Value))
                return;

            State = State.NoteLaneDrawing;
            mPart = part;
            mId = key.Value.Id;
            mEntry = part.GetNoteLaneEntry(key.Value);
            part.BeginMergeDirty();
            mHead = part.Head;
            mDownValue = AutomationRenderer.YToValue(position.Y, mEntry.MinValue, mEntry.MaxValue);
            mLastX = position.X;
            Apply(position, constantValue);
        }

        public void Move(Avalonia.Point position, bool constantValue)
        {
            if (!IsOperating)
                return;

            Apply(position, constantValue);
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            mPart!.EndMergeDirty();
            mPart.Commit();
            mPart = null;
            mValues.Clear();
            State = State.None;
        }

        void Apply(Avalonia.Point position, bool constantValue)
        {
            double value = constantValue ? mDownValue : AutomationRenderer.YToValue(position.Y, mEntry.MinValue, mEntry.MaxValue);
            double tick0 = AutomationRenderer.TickAxis.X2Tick(Math.Min(mLastX, position.X));
            double tick1 = AutomationRenderer.TickAxis.X2Tick(Math.Max(mLastX, position.X));
            mLastX = position.X;

            foreach (var (note, startTick, endTick) in NoteLaneRanges(mPart!))
            {
                if (endTick <= tick0)
                    continue;

                if (startTick > tick1)
                    break;

                mValues[note] = value;
            }

            mPart!.DiscardTo(mHead);
            foreach (var kvp in mValues)
                kvp.Key.Properties.SetValue(mId, PropertyValue.Create(kvp.Value));
        }

        IMidiPart? mPart;
        string mId = string.Empty;
        LaneEntry mEntry;
        double mDownValue;   // 定值绘制锁定的值（按下时捕获）
        double mLastX;
        Head mHead;
        readonly Dictionary<INote, double> mValues = new();
    }

    readonly NoteLaneDrawOperation mNoteLaneDrawOperation;

    // note lane 清除：右键横扫把扫过区间内的 note 属性值移除（回到插件默认值）——对偶连续轨的右键清除。
    // 同 DiscardTo 重放：区间只扩不缩，每帧对区间内 note 整批 RemoveValue，抬起 Commit 一个撤销步。
    class NoteLaneClearOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        // 与 phoneme lane 操作共用 State.NoteLaneClearing，靠 mPart 区分是否本操作在跑。
        public bool IsOperating => State == State.NoteLaneClearing && mPart != null;

        public void Down(double x)
        {
            if (State != State.None)
                return;

            var part = AutomationRenderer.Part;
            var key = AutomationRenderer.mDependency.ActiveAutomation;
            if (part == null || key == null || !part.IsEffectiveNoteLane(key.Value))
                return;

            State = State.NoteLaneClearing;
            mPart = part;
            mId = key.Value.Id;
            part.BeginMergeDirty();
            mHead = part.Head;
            double tick = AutomationRenderer.TickAxis.X2Tick(x);
            mStart = tick;
            mEnd = tick;
            Apply();
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            double tick = AutomationRenderer.TickAxis.X2Tick(x);
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            Apply();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            mPart!.EndMergeDirty();
            mPart.Commit();
            mPart = null;
            State = State.None;
        }

        void Apply()
        {
            mPart!.DiscardTo(mHead);
            foreach (var (note, startTick, endTick) in NoteLaneRanges(mPart))
            {
                if (endTick <= mStart)
                    continue;

                if (startTick > mEnd)
                    break;

                note.Properties.RemoveValue(mId);
            }
        }

        IMidiPart? mPart;
        string mId = string.Empty;
        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly NoteLaneClearOperation mNoteLaneClearOperation;

    // phoneme lane 绘制：主键拖动把扫过的显示音素属性值写为鼠标处值（Ctrl = 锁定按下值）。写回走音素编辑统一范式：
    // 受影响 note 先 LockPhonemes（幂等，合成→钉死保几何）再写该位音素 Properties；DiscardTo 重放、抬起单 Commit。
    // 命中几何与渲染共用 note.DisplayPhonemes（绝对秒、去重叠），目标按 (note, index) 记账——重放把钉死撤回后
    // 再 lock 重建，索引仍对齐（钉死列表逐位拷贝自合成音素、几何不变）。
    class PhonemeLaneDrawOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public bool IsOperating => State == State.NoteLaneDrawing && mPart != null;

        public void Down(Avalonia.Point position, bool constantValue)
        {
            if (State != State.None)
                return;

            var part = AutomationRenderer.Part;
            var key = AutomationRenderer.mDependency.ActiveAutomation;
            if (part == null || key == null || !part.IsEffectivePhonemeLane(key.Value))
                return;

            mEntry = part.GetPhonemeLaneEntry(key.Value);
            if (!mEntry.Resolved)
                return;   // 未解析占位（合成音素未产出）：量程未知，不可编辑

            State = State.NoteLaneDrawing;
            mPart = part;
            mId = key.Value.Id;
            part.BeginMergeDirty();
            mHead = part.Head;
            mDownValue = AutomationRenderer.YToValue(position.Y, mEntry.MinValue, mEntry.MaxValue);
            mLastX = position.X;
            Apply(position, constantValue);
        }

        public void Move(Avalonia.Point position, bool constantValue)
        {
            if (!IsOperating)
                return;

            Apply(position, constantValue);
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            mPart!.EndMergeDirty();
            mPart.Commit();
            mPart = null;
            mTargets.Clear();
            State = State.None;
        }

        void Apply(Avalonia.Point position, bool constantValue)
        {
            double value = constantValue ? mDownValue : AutomationRenderer.YToValue(position.Y, mEntry.MinValue, mEntry.MaxValue);
            var tempoManager = mPart!.TempoManager;
            double sec0 = tempoManager.GetTime(Math.Max(0, AutomationRenderer.TickAxis.X2Tick(Math.Min(mLastX, position.X))));
            double sec1 = tempoManager.GetTime(Math.Max(0, AutomationRenderer.TickAxis.X2Tick(Math.Max(mLastX, position.X))));
            mLastX = position.X;

            foreach (var note in mPart.Notes)
            {
                if (note.StartTime > sec1 + LeadExtensionSlack)
                    break;

                var phonemes = note.DisplayPhonemes;
                for (int i = 0; i < phonemes.Count; i++)
                {
                    if (phonemes[i].EndTime <= sec0 || phonemes[i].StartTime > sec1)
                        continue;

                    mTargets[(note, i)] = value;
                }
            }

            mPart.DiscardTo(mHead);
            foreach (var target in mTargets)
            {
                var note = target.Key.Note;
                note.LockPhonemes();   // 幂等：同 note 多目标重复调用无害
                if (target.Key.Index < note.Phonemes.Count)
                    note.Phonemes[target.Key.Index].Properties.SetValue(mId, PropertyValue.Create(target.Value));
            }
        }

        IMidiPart? mPart;
        string mId = string.Empty;
        LaneEntry mEntry;
        double mDownValue;   // 定值绘制锁定的值（按下时捕获）
        double mLastX;
        Head mHead;
        readonly Dictionary<(INote Note, int Index), double> mTargets = new();
    }

    readonly PhonemeLaneDrawOperation mPhonemeLaneDrawOperation;

    // phoneme lane 清除：右键横扫把扫过区间内【钉死音素】的该属性值移除（回到插件默认值）。
    // 未钉死音素本就显示默认值，清除不为其钉死（不为 no-op 制造数据）；HasProperties 闸门避免 lazy 物化。
    class PhonemeLaneClearOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public bool IsOperating => State == State.NoteLaneClearing && mPart != null;

        public void Down(double x)
        {
            if (State != State.None)
                return;

            var part = AutomationRenderer.Part;
            var key = AutomationRenderer.mDependency.ActiveAutomation;
            if (part == null || key == null || !part.IsEffectivePhonemeLane(key.Value))
                return;

            State = State.NoteLaneClearing;
            mPart = part;
            mId = key.Value.Id;
            part.BeginMergeDirty();
            mHead = part.Head;
            double sec = part.TempoManager.GetTime(Math.Max(0, AutomationRenderer.TickAxis.X2Tick(x)));
            mStart = sec;
            mEnd = sec;
            Apply();
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            double sec = mPart!.TempoManager.GetTime(Math.Max(0, AutomationRenderer.TickAxis.X2Tick(x)));
            mStart = Math.Min(mStart, sec);
            mEnd = Math.Max(mEnd, sec);
            Apply();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            mPart!.EndMergeDirty();
            mPart.Commit();
            mPart = null;
            State = State.None;
        }

        void Apply()
        {
            mPart!.DiscardTo(mHead);
            foreach (var note in mPart.Notes)
            {
                if (note.StartTime > mEnd + LeadExtensionSlack)
                    break;

                if (note.Phonemes.Count == 0)
                    continue;

                var phonemes = note.DisplayPhonemes;
                for (int i = 0; i < phonemes.Count && i < note.Phonemes.Count; i++)
                {
                    if (phonemes[i].EndTime <= mStart || phonemes[i].StartTime > mEnd)
                        continue;

                    if (note.Phonemes[i].HasProperties)
                        note.Phonemes[i].Properties.RemoveValue(mId);
                }
            }
        }

        IMidiPart? mPart;
        string mId = string.Empty;
        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly PhonemeLaneClearOperation mPhonemeLaneClearOperation;

    class MiddleDragOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public bool IsOperating => mIsDragging;

        public void Down(double x)
        {
            if (mIsDragging)
                return;

            mIsDragging = true;
            mDownTick = AutomationRenderer.TickAxis.X2Tick(x);
            AutomationRenderer.TickAxis.StopMoveAnimation();
        }

        public void Move(double x)
        {
            if (!mIsDragging)
                return;

            AutomationRenderer.TickAxis.MoveTickToX(mDownTick, x);
        }

        public void Up()
        {
            if (!mIsDragging)
                return;

            mIsDragging = false;
        }

        double mDownTick;
        bool mIsDragging = false;
    }

    readonly MiddleDragOperation mMiddleDragOperation;

    // 参数区的范围选区操作：与音符区共用同一条 tick 带（状态归 PianoScrollView）。本操作只算 x→tick（吸附复用 PianoScrollView）、
    // 写进 PianoScrollView.SetRegionSelection；纵向(值轴)与范围无关、贯穿全高。未拖(点击)不建区——清/留交给 OnMouseUp 点击阈值。
    class RegionSelectionOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public bool IsOperating => State == State.RegionSelecting;

        public void Down(double x, bool noSnap)
        {
            if (State != State.None || AutomationRenderer.Part == null)
                return;

            State = State.RegionSelecting;
            mDownTick = TickAt(x, noSnap);
            AutomationRenderer.mDependency.PianoScrollView.SetRegionSelection(mDownTick, mDownTick);
        }

        public void Move(double x, bool noSnap)
        {
            if (!IsOperating)
                return;

            AutomationRenderer.mDependency.PianoScrollView.SetRegionSelection(mDownTick, TickAt(x, noSnap));
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
            var pianoScrollView = AutomationRenderer.mDependency.PianoScrollView;
            if (pianoScrollView.CurrentRegionSelection is { } sel && sel.EndTick <= sel.StartTick)
                pianoScrollView.ClearRegionSelection();
        }

        double TickAt(double x, bool noSnap)
        {
            double tick = AutomationRenderer.TickAxis.X2Tick(x);
            if (!noSnap)
                tick = AutomationRenderer.mDependency.PianoScrollView.GetQuantizedTick(tick);
            return Math.Max(0, tick);
        }

        double mDownTick;
    }

    readonly RegionSelectionOperation mRegionSelectionOperation;

    class DrawOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.Drawing;

        public void Down(Avalonia.Point mousePosition, bool constantValue)
        {
            if (IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            var automationKey = AutomationRenderer.mDependency.ActiveAutomation;
            if (automationKey == null)
                return;

            mAutomation = AutomationRenderer.Part.GetEffectiveAutomation(automationKey.Value)
                ?? AutomationRenderer.Part.AddEffectiveAutomation(automationKey.Value);

            if (mAutomation == null)
                return;

            State = State.Drawing;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = mAutomation.Head;
            var config = AutomationRenderer.Part.GetEffectiveAutomationConfig(automationKey.Value);
            mMin = config.MinValue;
            mMax = config.MaxValue;
            mDownValue = mMax - (mousePosition.Y / AutomationRenderer.Bounds.Height) * (mMax - mMin);   // 锁定按下时的 y，供定值绘制

            mPointLines.Add([ToTickAndValue(mousePosition, constantValue)]);
            mAutomation.AddLine(mPointLines[0], Settings.ParameterBoundaryExtension);
        }

        public void Move(Avalonia.Point mousePosition, bool constantValue)
        {
            if (!IsOperating)
                return;

            var point = ToTickAndValue(mousePosition, constantValue);
            var lastLine = mPointLines.Last();
            var lastPoint = mDirection ? lastLine.Last() : lastLine.First();
            if (lastPoint.X == point.X)
            {
                if (lastPoint.Y == point.Y)
                    return;

                lastLine[mDirection ? lastLine.Count - 1 : 0] = point;
            }
            else
            {
                bool direction = point.X > lastPoint.X;
                if (lastLine.Count == 1)
                {
                    lastLine.Insert(direction ? 1 : 0, point);
                }
                else
                {
                    if (direction == mDirection)
                        lastLine.Insert(direction ? lastLine.Count : 0, point);
                    else
                        mPointLines.Add(direction ? [lastPoint, point] : [point, lastPoint]);
                }

                mDirection = direction;
            }

            mAutomation.DiscardTo(mHead);
            foreach (var line in mPointLines)
            {
                mAutomation.AddLine(line.Simplify(5, 2), Settings.ParameterBoundaryExtension);
            }
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            foreach (var line in mPointLines)
            {
                mAutomation.AddLine(line.Simplify(5, 2), Settings.ParameterBoundaryExtension);
            }
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            mPointLines.Clear();
            State = State.None;
        }

        TuneLab.Foundation.Point ToTickAndValue(Avalonia.Point mousePosition, bool constantValue)
        {
            double value = constantValue ? mDownValue : mMax - (mousePosition.Y / AutomationRenderer.Bounds.Height) * (mMax - mMin);
            return new(AutomationRenderer.TickAxis.X2Tick(mousePosition.X) - mAutomation!.Part.Pos.Value, value);
        }

        IAutomation? mAutomation = null;
        double mMax;
        double mMin;
        double mDownValue;   // 定值绘制锁定的值（按下时捕获）
        bool mDirection;
        Head mHead;
        readonly List<List<TuneLab.Foundation.Point>> mPointLines = new();
    }

    readonly DrawOperation mDrawOperation;

    class ClearOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.Clearing;

        public void Down(double x)
        {
            if (IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            var automationKey = AutomationRenderer.mDependency.ActiveAutomation;
            if (automationKey == null)
                return;

            mAutomation = AutomationRenderer.Part.GetEffectiveAutomation(automationKey.Value);
            if (mAutomation == null)
                return;

            State = State.Clearing;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = AutomationRenderer.Part.Head;
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - mAutomation.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            mAutomation.Clear(mStart, mEnd, Settings.ParameterBoundaryExtension);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            mAutomation.DiscardTo(mHead);
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - mAutomation.Part.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            mAutomation.Clear(mStart, mEnd, Settings.ParameterBoundaryExtension);
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            mAutomation.Clear(mStart, mEnd, Settings.ParameterBoundaryExtension);
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            State = State.None;
        }

        IAutomation? mAutomation = null;
        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly ClearOperation mClearOperation;

    class AnchorSelectOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        // 须同时判 mAutomation：分段轨选区操作也用 State.AnchorSelecting，靠 mAutomation 区分是否本（连续）操作在跑。
        public bool IsOperating => State == State.AnchorSelecting && mAutomation != null;

        public void Down(Avalonia.Point point, bool ctrl)
        {
            if (State != State.None)
                return;

            if (!AutomationRenderer.TryGetActiveAutomation(out mAutomation, out var config, false))
                return;

            State = State.AnchorSelecting;
            mMinValue = config.MinValue;
            mMaxValue = config.MaxValue;
            mDefaultValue = mAutomation.DefaultValue.Value;
            mDownTick = AutomationRenderer.TickAxis.X2Tick(point.X) - mAutomation.Part.Pos.Value;
            mDownValue = AutomationRenderer.YToValue(point.Y, mMinValue, mMaxValue);
            if (ctrl)
            {
                mSelectedItems = mAutomation.Points.AllSelectedItems();
            }
            Move(point);
        }

        public void Move(Avalonia.Point point)
        {
            if (!IsOperating || mAutomation == null)
                return;

            mTick = AutomationRenderer.TickAxis.X2Tick(point.X) - mAutomation.Part.Pos.Value;
            mValue = AutomationRenderer.YToValue(point.Y, mMinValue, mMaxValue);
            mAutomation.Points.DeselectAllItems();
            if (mSelectedItems != null)
            {
                foreach (var item in mSelectedItems)
                    item.Select();
            }

            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double minValue = Math.Min(mValue, mDownValue);
            double maxValue = Math.Max(mValue, mDownValue);
            foreach (var pointItem in mAutomation.Points)
            {
                double value = pointItem.Value + mDefaultValue;
                if (pointItem.Pos >= minTick && pointItem.Pos <= maxTick && value >= minValue && value <= maxValue)
                    pointItem.Select();
            }

            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
            mSelectedItems = null;
            mAutomation = null;
            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        public Rect SelectionRect()
        {
            if (mAutomation == null)
                return new Rect();

            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double minValue = Math.Min(mValue, mDownValue);
            double maxValue = Math.Max(mValue, mDownValue);
            double left = AutomationRenderer.TickAxis.Tick2X(mAutomation.Part.Pos.Value + minTick);
            double right = AutomationRenderer.TickAxis.Tick2X(mAutomation.Part.Pos.Value + maxTick);
            double top = AutomationRenderer.ValueToY(maxValue, mMinValue, mMaxValue);
            double bottom = AutomationRenderer.ValueToY(minValue, mMinValue, mMaxValue);
            return new Rect(left, top, right - left, bottom - top);
        }

        IAutomation? mAutomation;
        IReadOnlyCollection<AnchorPoint>? mSelectedItems;
        double mMinValue;
        double mMaxValue;
        double mDefaultValue;
        double mDownTick;
        double mDownValue;
        double mTick;
        double mValue;
    }

    readonly AnchorSelectOperation mAnchorSelectOperation;

    class AnchorDeleteOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.AnchorDeleting;

        public void Down(double x)
        {
            if (IsOperating)
                return;

            if (!AutomationRenderer.TryGetActiveAutomation(out mAutomation, out _, false))
                return;

            State = State.AnchorDeleting;
            AutomationRenderer.Part!.BeginMergeDirty();
            mHead = AutomationRenderer.Part.Head;
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - mAutomation.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            mAutomation.DeletePoints(mStart, mEnd);
            AutomationRenderer.UpdateAnchorValueInput();
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            mAutomation.DiscardTo(mHead);
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - mAutomation.Part.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            mAutomation.DeletePoints(mStart, mEnd);
            AutomationRenderer.UpdateAnchorValueInput();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            mAutomation.DeletePoints(mStart, mEnd);
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            State = State.None;
            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        IAutomation? mAutomation = null;
        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly AnchorDeleteOperation mAnchorDeleteOperation;

    class AnchorMoveOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public void Down(Avalonia.Point point, bool ctrl, IAutomation automation, AnchorPoint anchor, double minValue, double maxValue, bool keepChangeWithoutMove = false)
        {
            if (AutomationRenderer.Part == null)
                return;

            mAutomation = automation;
            mAnchor = anchor;
            mCtrl = ctrl;
            mIsSelected = anchor.IsSelected;
            mKeepChangeWithoutMove = keepChangeWithoutMove;
            mMin = minValue;
            mMax = maxValue;
            if (!mCtrl && !mIsSelected)
            {
                mAutomation.Points.DeselectAllItems();
            }
            anchor.Select();

            State = State.AnchorMoving;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = AutomationRenderer.Part.Head;
            mXOffset = point.X - AutomationRenderer.TickAxis.Tick2X(AutomationRenderer.Part.Pos.Value + anchor.Pos);
            mYOffset = point.Y - AutomationRenderer.ValueToY(anchor.Value + automation.DefaultValue.Value, mMin, mMax);
            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        public void Move(Avalonia.Point point)
        {
            var part = AutomationRenderer.Part;
            if (part == null || mAutomation == null || mAnchor == null)
                return;

            double pos = AutomationRenderer.TickAxis.X2Tick(point.X - mXOffset) - part.Pos.Value;
            double posOffset = pos - mAnchor.Pos;
            double value = AutomationRenderer.YToValue(point.Y - mYOffset, mMin, mMax);
            double valueOffset = value - (mAnchor.Value + mAutomation.DefaultValue.Value);

            mMoved = true;
            part.DiscardTo(mHead);
            mAutomation.MoveSelectedPoints(posOffset, valueOffset);
            AutomationRenderer.UpdateAnchorValueInput();
        }

        public void Up()
        {
            State = State.None;

            if (mAnchor == null || mAutomation == null)
                return;

            if (AutomationRenderer.Part == null)
                return;

            AutomationRenderer.Part.EndMergeDirty();
            if (mMoved || mKeepChangeWithoutMove)
            {
                AutomationRenderer.Part.Commit();
            }
            else
            {
                AutomationRenderer.Part.Discard();
                if (mCtrl)
                {
                    if (mIsSelected)
                    {
                        mAnchor.Inselect();
                    }
                }
                else
                {
                    mAutomation.Points.DeselectAllItems();
                    mAnchor.Select();
                }
            }

            mMoved = false;
            mKeepChangeWithoutMove = false;
            mAutomation = null;
            mAnchor = null;
            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        IAutomation? mAutomation;
        AnchorPoint? mAnchor;
        bool mCtrl;
        bool mIsSelected;
        bool mMoved = false;
        bool mKeepChangeWithoutMove = false;
        double mXOffset;
        double mYOffset;
        double mMin;
        double mMax;
        Head mHead;
    }

    readonly AnchorMoveOperation mAnchorMoveOperation;

    class VibratoAmplitudeOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public void Down(double y, Vibrato downVibrato)
        {
            if (AutomationRenderer.Part == null)
                return;

            if (!downVibrato.IsSelected)
            {
                AutomationRenderer.Part.Vibratos.DeselectAllItems();
                downVibrato.Select();
            }

            var vibratos = AutomationRenderer.Part.Vibratos.AllSelectedItems();
            if (vibratos.IsEmpty())
                return;

            var automationKey = AutomationRenderer.mDependency.ActiveAutomation;
            // vibrato 振幅调节：voice 与 effect 连续自动化皆可（按 key 路由到各自影响表）。
            if (automationKey == null)
                return;

            if (!AutomationRenderer.Part.IsEffectiveAutomation(automationKey.Value))
                return;

            State = State.VibratoAmplitudeAdjusting;
            AutomationRenderer.Part.BeginMergeDirty();
            mVibratos = vibratos;
            mKey = automationKey.Value;
            foreach (var vibrato in mVibratos)
            {
                if (!vibrato.IsAssociated(mKey))
                    vibrato.SetAmplitude(mKey, 0);
            }
            mHead = AutomationRenderer.Part.Head;
            var config = AutomationRenderer.Part.GetEffectiveAutomationConfig(automationKey.Value);
            mMin = config.MinValue;
            mMax = config.MaxValue;
            mValue = ValueAt(y);
        }

        public void Move(double y)
        {
            if (AutomationRenderer.Part == null)
                return;

            if (mVibratos == null)
                return;

            AutomationRenderer.Part.DiscardTo(mHead);
            double value = ValueAt(y);
            double offset = value - mValue;
            foreach (var vibrato in mVibratos)
            {
                vibrato.SetAmplitude(mKey, vibrato.GetAmplitude(mKey) + offset);
            }
        }

        public void Up()
        {
            if (AutomationRenderer.Part == null)
                return;

            State = State.None;
            var head = AutomationRenderer.Part.Head;
            AutomationRenderer.Part.EndMergeDirty();
            if (head == mHead)
            {
                AutomationRenderer.Part.Discard();
            }
            else
            {
                AutomationRenderer.Part.Commit();
            }
            mVibratos = null;
        }

        double ValueAt(double y)
        {
            return mMax - (y / AutomationRenderer.Bounds.Height) * (mMax - mMin);
        }

        IReadOnlyCollection<Vibrato>? mVibratos;
        AutomationKey mKey;
        double mValue;
        double mMax;
        double mMin;
        Head mHead;
    }

    readonly VibratoAmplitudeOperation mVibratoAmplitudeOperation;

    enum State
    {
        None,
        Drawing,
        Clearing,
        AnchorSelecting,
        AnchorDeleting,
        AnchorMoving,
        VibratoAmplitudeAdjusting,
        RegionSelecting,
        NoteLaneDrawing,
        NoteLaneClearing,
    }

    State mState = State.None;

    // 主键按下点 + 点击判定阈值：抬起时位移 ≤ 阈值即视为"点击(未拖)"，用于清空范围选区。镜像音符区 PianoScrollViewOperation。
    Avalonia.Point mPrimaryDownPos;
    const double ClickThreshold = 4;
}
