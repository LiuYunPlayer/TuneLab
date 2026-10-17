using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

// 效果器侧栏：目标 part 的效果器链。从 Part 面板拆出来单成一面——拆分边界按 preset 的作用域划：
// 自动化默认值随声源声明、是 part preset 的一部分，留在 Part 面板；效果器链与 part preset 正交
//（声源域 / 处理域），将来有它自己的链 preset，面板头就是它的位置。
//
// 目标 part 与 Part 面板同一份（宿主按焦点感知一并下发，见 Editor.UpdatePartPanelTarget），故两面并排时
// 看的总是同一批 part；顶栏标题也与 Part 面板同口径（编辑中 / 选中 N 个），靠图标区分是哪一面。
internal class EffectsSideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Effects.GetImage(Style.LIGHT_WHITE), Name = Title, Items = [mContent] };

    public event Action? TitleChanged;
    public string Title { get; private set; } = "Effects".Tr(TC.Property);

    public EffectsSideBarContentProvider()
    {
        mContent.Children.Add(mEffectsController);
    }

    public void SetParts(IReadOnlyList<IMidiPart> parts, PartPanelSource source)
    {
        s.DisposeAll();
        mParts = parts;
        mSource = source;
        foreach (var part in parts)
            part.Name.Modified.Subscribe(RaiseTitleChanged, s);

        // 没有目标 part 时整块不显示（效果链是挂在 part 上的，没有 part 就没有「加效果器」可言）。
        mEffectsController.IsVisible = parts.Count > 0;
        mEffectsController.SetParts(parts);
        RaiseTitleChanged();
    }

    void RaiseTitleChanged()
    {
        Title = mParts.Count == 0 ? "Effects".Tr(TC.Property) : PartPropertySideBarContentProvider.TitleOf(mParts, mSource);
        TitleChanged?.Invoke();
    }

    readonly StackPanel mContent = new() { Orientation = Orientation.Vertical };
    readonly EffectsController mEffectsController = new();
    IReadOnlyList<IMidiPart> mParts = [];
    PartPanelSource mSource = PartPanelSource.Current;
    readonly DisposableManager s = new();
}
