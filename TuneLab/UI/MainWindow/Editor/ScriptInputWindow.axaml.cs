using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.UI;

// 脚本入参窗：复用属性面板控制器（PropertyObjectController）渲染 getInputs 的 config schema；用户改任一值即重算
// schema 并 keyed-diff（响应式条件字段）。数据挂独立 DataDocument，与工程 undo 隔离。确定返回填好的值(PropertyObject)、
// 取消返回 null。见 docs/script-inputs-and-action-surface.md §2.3。
internal partial class ScriptInputWindow : Window
{
    readonly PropertyObjectController mController = new();
    readonly DataPropertyObject mData;
    // 给当前已填值算 schema（内部调 ScriptRunner.GetInputSchema）；返回 (schema, error)，error 时保留当前 schema。
    readonly Func<PropertyObject, (ObjectConfig? Schema, string? Error)> mCompute;
    bool mReconcilePending;

    // initialSchema 由调用方以初值算好（保证非空、避免开窗即闪）；compute 供后续响应式重算。
    public ScriptInputWindow(string title, ObjectConfig initialSchema, Func<PropertyObject, (ObjectConfig? Schema, string? Error)> compute, PropertyObject initialValues)
    {
        InitializeComponent();
        Focusable = true;
        CanResize = true;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;

        TitleLabel.Content = title;
        Title = title + " - TuneLab";
        Background = Style.BACK.ToBrush();
        TitleLabel.Foreground = Style.TEXT_LIGHT.ToBrush();
        Content.Background = Style.INTERFACE.ToBrush();

        mCompute = compute;

        var closeButton = new Button() { Width = 48, Height = 40 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
        closeButton.Clicked += () => Close(null);
        WindowControl.Children.Add(closeButton);

        var titleBar = this.FindControl<Grid>("TitleBar") ?? throw new InvalidOperationException("TitleBar not found");
        bool useSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        if (useSystemTitle) { titleBar.Height = 0; Height -= 40; }

        // 数据挂独立文档（与工程 undo 隔离）；灌入初值（上次值/默认；缺项由控件按 config 默认兜底）。
        mData = new DataPropertyObject(new DataDocument());
        foreach (var kvp in initialValues.Map)
            mData.SetValue(kvp.Key, kvp.Value);
        mData.Commit();

        mController.SetConfig(initialSchema, mData);
        BodyScroll.Content = mController;

        // 值改动 → 重算 schema → reconcile。deferred 合帧：commit 可能发生在控件事件回调链内（如 ComboBox 的
        // SelectionChanged），同步重算会重入控件集合；pending 标志合并一拍内多次触发。
        mData.Modified.Subscribe(ScheduleReconcile);

        // 重置：圆形回转箭头图标按钮（省地方、不裁字），tooltip 说明。
        var resetButton = new Button() { Width = 32, Height = 28 };
        resetButton.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.15), PressedColor = Colors.White.Opacity(0.15) } });
        resetButton.AddContent(new() { Item = new IconItem() { Icon = Assets.Reset }, ColorSet = new() { Color = Style.LIGHT_WHITE } });
        ToolTip.SetTip(resetButton, "Reset to Default".Tr(TC.Dialog));
        resetButton.Clicked += ResetToDefault;
        var resetPanel = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        resetPanel.Children.Add(resetButton);
        ActionsPanel.Children.Add(resetPanel);
        Grid.SetColumn(resetPanel, 0);

        var cancelButton = MakeButton("Cancel".Tr(TC.Dialog), primary: false);
        cancelButton.Clicked += () => Close(null);
        var okButton = MakeButton("OK".Tr(TC.Dialog), primary: true);
        // 交出【稀疏】用户值（只含改过的键），与 getInputs 的 ctx.values 同形。缺键=未设，脚本读时自己补默认
        //（像 voice 插件读 props.GetValue(key, 默认)）；config 的 DefaultValue 只管显示与重置。见 docs §2.4。
        okButton.Clicked += () => Close(mData.GetInfo());
        var rightPanel = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        rightPanel.Children.Add(cancelButton);
        rightPanel.Children.Add(okButton);
        ActionsPanel.Children.Add(rightPanel);
        Grid.SetColumn(rightPanel, 2);

        Opened += (s, e) => { Activate(); okButton.Focus(); };
    }

    void ScheduleReconcile()
    {
        if (mReconcilePending)
            return;
        mReconcilePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mReconcilePending = false;
            var (schema, _) = mCompute(mData.GetInfo());
            if (schema != null)
                mController.Reconcile(schema);   // 出错则保留当前 schema，不打断编辑
        });
    }

    // 重置：删掉所有已设键，回到 pristine 稀疏态——控件经 DoubleField 兜底显示 config 默认，一次 Commit 触发 reconcile。
    // 不写默认进数据（保持稀疏一致：重置=清空用户意图，而非把默认 materialize 成 present 值）。
    void ResetToDefault()
    {
        var keys = new List<string>();
        foreach (var kvp in mData.GetInfo().Map)
            keys.Add(kvp.Key);
        foreach (var key in keys)
            mData.RemoveValue(key);
        mData.Commit();
    }

    static Button MakeButton(string text, bool primary)
    {
        var button = new Button() { MinWidth = 72, Height = 28 };
        button.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = primary ? Style.BUTTON_PRIMARY : Style.BUTTON_NORMAL, HoveredColor = primary ? Style.BUTTON_PRIMARY_HOVER : Style.BUTTON_NORMAL_HOVER } });
        button.AddContent(new() { Item = new TextItem() { Text = text }, ColorSet = new() { Color = primary ? Colors.White : Style.LIGHT_WHITE } });
        return button;
    }
}
