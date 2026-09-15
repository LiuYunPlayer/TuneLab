using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.I18N;
using TuneLab.Input;

namespace TuneLab.ScreenshotBot;

// 从运行中的注册表导出资料表，手册里的表格由此生成——避免手抄与代码脱节。
internal static class Dump
{
    public static void Keybindings(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var sb = new StringBuilder();
        sb.AppendLine("<!-- 由 tools/ScreenshotBot 从运行中的 Keymap 注册表导出，勿手改。 -->");
        sb.AppendLine();
        sb.AppendLine("| 命令 | 默认快捷键 | 作用域 | 命令 id |");
        sb.AppendLine("|------|-----------|--------|---------|");
        // 注册序 = 表格行序（与 keybinding list 命令同一口径，见 KeybindingCommands 的排序）。
        foreach (var command in Keymap.Bindings.OrderBy(c => ActionRegistry.OrderOf(c.ActionId)))
        {
            var gesture = Keymap.Effective(command.ActionId);
            sb.AppendLine($"| {command.DisplayName()} | {(gesture == null ? "—" : gesture.Value.ToDisplayString())} | {command.Scope} | `{command.ActionId}` |");
        }
        File.WriteAllText(full, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"[dump] {full}");
    }
}
