using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;   // ToBrush
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.UI;

// 设置窗「通用」页末尾的那一块：把【外部 AI agent 接入】这件事的门槛压到"点一下复制"。
//
// 为什么它在设置窗里而不是别处：接入要过的那道闸门（命令桥开关）就在这一页，用户在这里犯的疑问
// 恰好是"开了之后我该怎么告诉我的 agent"。为什么它不是一条"设置"：它没有值可存，只是一段动作。
internal partial class SettingsWindow : Window
{
    Control BuildExternalAgentBlock()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new(24, 24, 24, 12) };

        panel.Children.Add(new TextBlock
        {
            Text = "Connect an external AI agent".Tr(this),
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            FontWeight = FontWeight.Bold,
            Margin = new(0, 0, 0, 6),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Copy what an external agent needs to drive TuneLab: the command's real path on this machine, what it may and may not do, and how it must ask you for permission. Paste it into your agent.".Tr(this),
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            Opacity = 0.6,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 0, 0, 10),
        });

        // 复制完给一句确认。刻意不做"两秒后变回去"：定时改文案要么带走一个 timer，要么在窗关掉之后
        // 还在动 UI，而这里要的只是"我点到了"这一个信息。
        var done = new TextBlock
        {
            Text = string.Empty,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            Opacity = 0.6,
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new(12, 0, 0, 0),
        };

        var copy = new Button { Height = 28, MinWidth = 132 };
        copy.AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER } });
        copy.AddContent(new() { Item = new TextItem() { Text = "Copy instructions".Tr(this), FontSize = 12 }, ColorSet = new() { Color = Style.LIGHT_WHITE } });
        copy.Clicked += () => _ = CopyExternalAgentInstructionsAsync(done);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(copy);
        row.Children.Add(done);
        panel.Children.Add(row);
        return panel;
    }

    // 【文案在点下去的那一刻才生成】它含命令行的真实绝对路径、`tunelab` 到底能不能直接敲、以及命令桥
    // 此刻开没开——写死一段就会在三件事里各错一件（见 ExternalAgentOnboarding）。
    async Task CopyExternalAgentInstructionsAsync(TextBlock done)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                done.Text = "Could not reach the clipboard.".Tr(this);
                return;
            }
            await clipboard.SetTextAsync(ExternalAgentOnboarding.Build());
            done.Text = "Copied.".Tr(this);
        }
        catch (Exception ex)
        {
            // 剪贴板会失败（别的进程正占着它）。这条链路挂在按钮回调上，异常没人接就是整个进程带走，
            // 故就地接住并如实说一句。
            Log.Info("Failed to copy the external agent instructions: " + ex.Message);
            done.Text = "Could not reach the clipboard.".Tr(this);
        }
    }
}
