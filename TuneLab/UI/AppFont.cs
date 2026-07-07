using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using TuneLab.Configs;

namespace TuneLab.UI;

// 用户自选界面字体的实时应用点。
// 控件树：把窗口根 FontFamily 绑到 Settings.InterfaceFontFamily，未显式设字体的文字经继承实时跟随
//（编排区/侧栏/菜单/导出面板等全是子控件，一次覆盖整片）。
// 自绘文字：DrawString 处改读 AppFont.Current（而非 Typeface.Default，那只认启动时的 FontManager 默认、改后须重启），
// 字体变更时强制重绘整棵可视树补上（控件树刷新不会自动触发自绘层重绘）。
// 空值 = Inter：即 WithInterFont 注入的应用基准观感，使「系统默认」也能实时还原。
internal static class AppFont
{
    static readonly FontFamily Default = new("Inter");

    public static FontFamily Current
        => string.IsNullOrWhiteSpace(Settings.InterfaceFontFamily.Value) ? Default : new FontFamily(Settings.InterfaceFontFamily.Value);

    public static Typeface Typeface => new(Current);

    // 绑定窗口根字体到设置并随其实时刷新；窗口关闭即退订（对 app 生命周期的主窗口无害）。
    public static void Bind(Window window)
    {
        void Refresh()
        {
            window.FontFamily = Current;
            // 裸 DrawString（未显式传 typeface）的默认字体集中在此切换，使参数标题栏/曲线区等自绘文本随界面字体走。
            Utils.Extensions.DrawStringTypeface = Typeface;
            // 自绘层不随 FontFamily 变更自动重绘——整树 InvalidateVisual 一次（字体变更是低频操作，开销可接受）。
            window.InvalidateVisual();
            foreach (var visual in window.GetVisualDescendants())
                if (visual is Control control)
                    control.InvalidateVisual();
        }

        Refresh();
        Settings.InterfaceFontFamily.Modified.Subscribe(Refresh);
        window.Closed += (_, _) => Settings.InterfaceFontFamily.Modified.Unsubscribe(Refresh);
    }
}
