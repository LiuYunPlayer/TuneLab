using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TuneLab.Data;
using TuneLab.Extensions.Derivers;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace TuneLab.UI;

// deriver 参数对话框（run-inputs）：复用属性面板控制器渲染引擎的 GetPropertyConfig(context)。
// 反应式：用户改任一值 → 按当前情境（含源音频元信息）重算 config → keyed-diff 到控件树（条件字段随值显隐），
// 与脚本入参窗 / voice / effect 同范式。确定返回填好的参数值（PropertyObject），取消返回 null。
// 数据挂独立 DataDocument，与工程 undo 隔离。裁剪/落点是 apply-side、不在本窗。
internal sealed class DerivationDialog : Window
{
    readonly DataPropertyObject mData;
    readonly PropertyObjectController mController = new();
    readonly string mEngineId;
    bool mReconcilePending;

    public DerivationDialog(string engineDisplayName, string engineId)
    {
        mEngineId = engineId;

        Title = engineDisplayName + " - TuneLab";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        MinHeight = 140;
        MaxHeight = 640;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Style.INTERFACE.ToBrush();

        mData = new DataPropertyObject(new DataDocument());
        mController.SetConfig(ComputeConfig(), mData);
        mData.Modified.Subscribe(ScheduleReconcile);   // 反应式重算（合帧，见 ScheduleReconcile）

        var body = new StackPanel { Orientation = Orientation.Vertical, Margin = new(16), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = engineDisplayName, Foreground = Style.TEXT_LIGHT.ToBrush(), FontSize = 14, FontWeight = FontWeight.Bold });
        body.Children.Add(mController);

        var cancel = MakeButton("Cancel".Tr(TC.Dialog), primary: false);
        cancel.Clicked += () => Close(null);
        var ok = MakeButton("Derive".Tr(TC.Dialog), primary: true);
        ok.Clicked += () => Close(mData.GetInfo());
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(16, 0, 16, 16) };
        actions.Children.Add(cancel);
        actions.Children.Add(ok);

        var root = new DockPanel();
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        root.Children.Add(scroll);
        Content = root;

        Opened += (s, e) => { Activate(); ok.Focus(); };
    }

    ObjectConfig ComputeConfig()
        => DeriversManager.GetPropertyConfig(mEngineId, new AudioDerivationContext { Properties = mData.GetInfo() });

    // 合帧重算：commit 可能发生在控件事件回调链内，同步重算会重入控件集合；pending 标志合并一拍内多次触发。
    void ScheduleReconcile()
    {
        if (mReconcilePending)
            return;
        mReconcilePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mReconcilePending = false;
            mController.Reconcile(ComputeConfig());
        });
    }

    static Button MakeButton(string text, bool primary)
    {
        var button = new Button() { MinWidth = 72, Height = 28 };
        button.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = primary ? Style.BUTTON_PRIMARY : Style.BUTTON_NORMAL, HoveredColor = primary ? Style.BUTTON_PRIMARY_HOVER : Style.BUTTON_NORMAL_HOVER } });
        button.AddContent(new() { Item = new TextItem() { Text = text }, ColorSet = new() { Color = primary ? Colors.White : Style.LIGHT_WHITE } });
        return button;
    }
}
