using System;

namespace TuneLab.Commands;

// 【有界面的宿主进程】里那份共同的执行环境：当前工程、界面语言、用户此刻在编辑器里看着什么、主线程调度器。
//
// 这四样与"谁在调命令"无关——侧栏 agent 与命令桥（CLI attach）看到的必须是同一个工程、同一个当前 part，
// 否则用户开着界面让外部 agent 干活时，两边会各说各的。故由编辑器装上一次，各入口从这里取。
//
// 剩下两样【按入口不同】，不在这里给，由各入口自己补上（见 docs/command-surface.md §5）：
//  · Authorization —— 侧栏用用户的授权档位 + 内联卡片；桥按这次连接声明的档位；headless 必须显式给；
//  · SideModel —— 只有侧栏 agent 有模型可调，其余入口一律 null（那边按 §5.3 降级）。
internal static class HostCommandContext
{
    // 由编辑器在构造时装上（它持有工程与编辑器态）。取【访问器】而非快照：工程切换、用户切 part
    // 都不需要重装。
    public static Func<CommandContext>? Provider { get; set; }

    // 没装（还没开编辑器 / 无 UI 的测试进程）就给一个空环境——需要工程的命令自己会如实报"没有工程"，
    // 好过在这里抛异常：那会把"宿主还没就绪"变成一条看不懂的崩溃。
    public static CommandContext Current => Provider?.Invoke() ?? new CommandContext();
}
