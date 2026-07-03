using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.Data;
using TuneLab.I18N;
using TuneLab.Utils;
using TuneLab.SDK;

namespace TuneLab.UI;

internal class ParameterTabBar : Panel
{
    public event Action<AutomationKey, ParameterButton.ButtonState>? StateChangeAsked;
    public ParameterButton PitchButton => mPitchButton;

    public interface IDependency
    {
        event Action? ActiveAutomationChanged;
        event Action? VisibleAutomationChanged;
        IHolder<IMidiPart> PartHolder { get; }
        AutomationKey? ActiveAutomation { get; }
        bool IsAutomationVisible(AutomationKey automation);
    }

    public ParameterTabBar(IDependency dependency)
    {
        mDependency = dependency;

        Background = Back.ToBrush();

        mPitchButton = new ParameterButton(Color.Parse(ConstantDefine.PitchColor), ConstantDefine.PitchName);
        mPitchButton.State = ParameterButton.ButtonState.Visible;
        mPitchButton.StateChangeAsked += (state) => { mPitchButton.State = state; };

        mAutomationLayout = new() { Orientation = Orientation.Horizontal, HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, 9) };;
        Children.Add(mAutomationLayout);

        mDependency.PartHolder.Modified.Subscribe(OnPartChanged, s);
        mDependency.PartHolder.When(p => p.SoundSource.Modified).Subscribe(OnAutomationConfigsChanged, s);
        mDependency.PartHolder.When(p => p.Effects.MembershipModified).Subscribe(OnAutomationConfigsChanged, s);
        // 条件自动化轨随参数 commit 显隐 → 重建轨按钮（仅轨集合实变时触发）。
        mDependency.PartHolder.When(p => p.AutomationConfigsModified).Subscribe(OnAutomationConfigsChanged, s);
        mDependency.ActiveAutomationChanged += SyncAutomationButtonsState;
        mDependency.VisibleAutomationChanged += SyncAutomationButtonsState;

        OnPartChanged();
    }

    ~ParameterTabBar()
    {
        s.DisposeAll();
        mDependency.ActiveAutomationChanged -= SyncAutomationButtonsState;
        mDependency.VisibleAutomationChanged -= SyncAutomationButtonsState;
    }

    public void SetState(AutomationKey automation, ParameterButton.ButtonState state)
    {
        if (mAutomationButtons.ContainsKey(automation))
            mAutomationButtons[automation].State = state;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        mAutomationLayout.Measure(availableSize);
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mAutomationLayout.Arrange(new Rect(finalSize));
        return finalSize;
    }

    // voice 与各 effect 的自动化轨统一汇入这一栏。voice 一组，每个 effect 一组，组间插竖向分隔符让用户看出分组。
    // 轨用 AutomationKey 标识（来源 + plain id），避免与真实 id 撞名。
    void ResetAutomationButtons()
    {
        mAutomationButtons.Clear();
        mAutomationLayout.Children.Clear();

        if (Part == null)
            return;

        bool firstGroup = true;
        AddAutomationGroup(Part.SoundSource.AutomationConfigs, -1, ref firstGroup);
        AddLaneButtons(Part.NoteLaneConfigs, AutomationKey.NoteLane);
        AddLaneButtons(Part.PhonemeLaneConfigs, AutomationKey.PhonemeLane);
        for (int i = 0; i < Part.Effects.Count; i++)
            AddAutomationGroup(Part.Effects[i].AutomationConfigs, i, ref firstGroup);
    }

    // 属性 lane 按钮（note 组在前、phoneme 组随后）：与 voice 轨同源（钉选的声源属性），故并入 voice 组尾部、不插分隔符。
    // 轨色是宿主钉选时分配的（config 无色概念），名字沿用属性声明的 DisplayText。
    void AddLaneButtons(IReadOnlyOrderedMap<PropertyKey, LaneEntry> lanes, Func<string, AutomationKey> makeKey)
    {
        foreach (var kvp in lanes)
        {
            var key = makeKey(kvp.Key.Id);
            if (!mCacheParameterButtons.TryGetValue(key, out var button))
            {
                button = new ParameterButton(Color.Parse(kvp.Value.Color), kvp.Key.DisplayText ?? kvp.Key.Id) { Margin = new(6, 0) };
                var captured = key;
                button.StateChangeAsked += (state) => StateChangeAsked?.Invoke(captured, state);
                // lane 按钮右键可就地解钉——与侧栏属性行右键对称；也是「引擎不再声明该属性」时死 tab 的唯一移除口
                //（此时侧栏没有对应控件可右键）。只在创建时挂一次（缓存按钮跨重建复用）。
                var capturedButton = button;
                button.ContextRequested += (_, e) =>
                {
                    var part = Part;
                    if (part == null)
                        return;
                    var scope = captured.IsPhonemeLane ? ParameterPinKind.PhonemeProperty : ParameterPinKind.NoteProperty;
                    var menu = new ContextMenu();
                    menu.Items.Add(new MenuItem().SetName("Remove from Parameter Panel".Tr(TC.Menu)).SetAction(() =>
                    {
                        ParameterPinning.Unpin(part.SoundSource, scope, captured.Id);
                        part.RefreshPinnedLaneConfigs();
                    }));
                    menu.Open(capturedButton);
                    e.Handled = true;
                };
                mCacheParameterButtons.Add(key, button);
            }
            else
            {
                // 解钉后重钉可能分配到不同轨色（也可能切语言换 DisplayText）：缓存按钮就地对齐。
                button.Color = Color.Parse(kvp.Value.Color);
                button.Text = kvp.Key.DisplayText ?? kvp.Key.Id;
            }
            mAutomationButtons.Add(key, button);
            mAutomationLayout.Children.Add(button);
        }
    }

    // 一个来源（voice / 某 effect）一组（连续轨与分段轨同在此 map、按声明序），组间插分隔符。按钮形态与 kind 无关（读 config 色/名）。
    void AddAutomationGroup(IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs, int effectIndex, ref bool firstGroup)
    {
        if (configs.Count == 0)
            return;

        if (!firstGroup)
            mAutomationLayout.Children.Add(CreateSeparator());
        firstGroup = false;

        foreach (var kvp in configs)
        {
            var config = kvp.Value;
            var key = effectIndex < 0 ? AutomationKey.Voice(kvp.Key.Id) : AutomationKey.Effect(effectIndex, kvp.Key.Id);
            if (!mCacheParameterButtons.TryGetValue(key, out var button))
            {
                button = new ParameterButton(Color.Parse(config.Color), kvp.Key.DisplayText ?? kvp.Key.Id) { Margin = new(6, 0) };
                var captured = key;
                button.StateChangeAsked += (state) => StateChangeAsked?.Invoke(captured, state);
                mCacheParameterButtons.Add(key, button);
            }
            mAutomationButtons.Add(key, button);
            mAutomationLayout.Children.Add(button);
        }
    }

    static Control CreateSeparator()
    {
        return new Border()
        {
            Width = 1,
            Height = 16,
            Margin = new Thickness(8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = Style.LIGHT_WHITE.Opacity(0.25).ToBrush(),
        };
    }

    void SyncAutomationButtonsState()
    {
        if (Part == null)
            return;

        var activeAutomation = mDependency.ActiveAutomation;

        void SyncKey(AutomationKey key)
        {
            if (activeAutomation == key)
                SetState(key, ParameterButton.ButtonState.Edit);
            else
                SetState(key, mDependency.IsAutomationVisible(key) ? ParameterButton.ButtonState.Visible : ParameterButton.ButtonState.Off);
        }

        void Sync(IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs, int effectIndex)
        {
            foreach (var kvp in configs)
                SyncKey(effectIndex < 0 ? AutomationKey.Voice(kvp.Key.Id) : AutomationKey.Effect(effectIndex, kvp.Key.Id));
        }

        Sync(Part.SoundSource.AutomationConfigs, -1);
        foreach (var kvp in Part.NoteLaneConfigs)
            SyncKey(AutomationKey.NoteLane(kvp.Key.Id));
        foreach (var kvp in Part.PhonemeLaneConfigs)
            SyncKey(AutomationKey.PhonemeLane(kvp.Key.Id));
        for (int i = 0; i < Part.Effects.Count; i++)
            Sync(Part.Effects[i].AutomationConfigs, i);
    }

    void OnPartChanged()
    {
        OnAutomationConfigsChanged();
    }

    void OnAutomationConfigsChanged()
    {
        ResetAutomationButtons();
        SyncAutomationButtonsState();
    }

    public double AutoHeight => mAutomationLayout.DesiredSize.Height;
    IMidiPart? Part => mDependency.PartHolder.Value;

    Color Back => Style.INTERFACE;

    readonly WrapPanel mAutomationLayout;
    readonly ParameterButton mPitchButton;
    readonly OrderedMap<AutomationKey, ParameterButton> mAutomationButtons = new();
    readonly Map<AutomationKey, ParameterButton> mCacheParameterButtons = new();

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
