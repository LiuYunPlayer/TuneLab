using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.UI;

// 属性侧栏的 voice 自动化「默认值」编辑：每条连续自动化一行 slider。
// （effect 的自动化默认值不在此——已并入各 effect 自己的参数块，见 EffectsController。）
// 共用 AutomationDefaultRow 承载每行的合并脏 + 按需 AddAutomation 语义。
internal class AutomationDefaultsController : StackPanel
{
    public IMidiPart? Part { get => mPart; set { mPart = value; Rebuild(); } }

    public AutomationDefaultsController()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
    }

    void Rebuild()
    {
        s.DisposeAll();
        foreach (var row in mRows)
            row.Dispose();
        mRows.Clear();
        Children.Clear();

        if (mPart == null)
            return;

        // 默认值面板只列连续轨（分段轨无默认基线）。
        foreach (var kvp in mPart.Voice.AutomationConfigs)
        {
            if (kvp.Value.IsPiecewise)
                continue;

            var row = new AutomationDefaultRow(mPart, AutomationKey.Voice(kvp.Key.Id), kvp.Key.DisplayText ?? kvp.Key.Id, kvp.Value);
            mRows.Add(row);
            Children.Add(row);
        }

        // 默认值外部（undo/redo/preset）改动 → 刷新所有行；自动化轨增减 → 重建；
        // 条件自动化轨随参数 commit 显隐 → 重建（仅轨集合实变时触发）。
        mPart.Automations.WhenAny(automation => automation.DefaultValue.Modified).Subscribe(Refresh, s);
        mPart.AutomationConfigsModified.Subscribe(Rebuild, s);
    }

    void Refresh()
    {
        foreach (var row in mRows)
            row.Refresh();
    }

    IMidiPart? mPart = null;
    readonly List<AutomationDefaultRow> mRows = new();
    readonly DisposableManager s = new();
}
