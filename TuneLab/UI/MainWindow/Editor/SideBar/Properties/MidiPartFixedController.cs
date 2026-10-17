using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.I18N;

namespace TuneLab.UI;

internal class MidiPartFixedController : StackPanel
{
    public MidiPartFixedController()
    {
        Background = Style.INTERFACE.ToBrush();
        Orientation = Avalonia.Layout.Orientation.Vertical;

        AddController(mGainController, "Gain".Tr(TC.Property));
        mGainController.SetRange(-24, +24);
        mGainController.SetDefaultValue(0);
    }

    // 绑定到目标 part 集（单/多/空统一走 MultipleDataProperty：空→滑块 Invalid、单→等价单绑、多→三态合并 + 写扇出）。
    public void SetParts(IReadOnlyList<IMidiPart> parts)
    {
        s.DisposeAll();
        var gain = new MultipleDataProperty<double>(parts.Select(part => part.Gain).ToList(), 0.0, value => PropertyValue.Create(value));
        mGainController.BindDataProperty(gain, s);
        gain.Modified.Subscribe(mGainFold.RefreshSummary, s);   // 撤销 / 预设等外部改值也刷新收起时的摘要
        mGainFold.RefreshSummary();
    }

    // 增益是宿主自带字段、不归任何插件，折叠记忆用固定键。收起时标题行显示当前值。
    void AddController(SliderController control, string name)
    {
        var title = new Avalonia.Controls.Label()
        {
            Height = 30,
            FontSize = 12,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,   // 同属性面板字段标题：文字居中
            Foreground = Style.TEXT_LABEL.ToBrush(),   // 同属性面板字段标签
            Content = name,
            Padding = new(24, 0)
        };
        mGainFold = new FieldFold(title, [control], "part/@gain",
            summary: () => double.IsNaN(control.Value) ? null : control.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        control.ValueChanged.Subscribe(mGainFold.RefreshSummary);   // 拖动中实时跟
        Children.Add(mGainFold.Header);
        Children.Add(control);   // 字段之间不画线（只有分组边界有线，见 SideBarSection）
    }

    FieldFold mGainFold = null!;   // 构造期 AddController 必先行

    readonly SliderController mGainController = new() { Margin = new(24, 5, 24, 12) };   // 上边距同属性面板字段（标题文字 → 控件 12）
    readonly DisposableManager s = new();
}
