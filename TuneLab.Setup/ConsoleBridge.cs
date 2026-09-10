using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace TuneLab.Setup;

/// <summary>
/// 把 stdout/stderr 接回**启动它的那个控制台**。
///
/// 安装器是 GUI 子系统的 exe（WinExe），没有自带控制台：`Console.Error.WriteLine` 在 cmd 里
/// 一个字都看不见——命令行模式下的用法错、失败原因也就无从得知（这正是 SilentRunner 当初改写
/// 日志文件的原因）。附着到父进程的控制台之后，那些话才真的到得了敲命令的人眼前。
///
/// 只在**带命令行参数启动**时调用：双击运行的场景没有父控制台，附着必然失败，也不该白试。
/// GUI 子系统的程序附着后 shell 提示符可能先于输出返回（父 shell 不等它），这是这类程序的常态，
/// 脚本按退出码判断即可。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ConsoleBridge
{
    const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(int processId);

    public static void AttachIfPossible()
    {
        try
        {
            if (!AttachConsole(AttachParentProcess))
                return;   // 没有父控制台（双击启动 / 被 GUI 拉起）：静默放弃

            // 附着之后要重新拿一次句柄：进程启动时 Console 已经绑到了"无控制台"的空设备上。
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
        catch (Exception)
        {
            // 附不上就算了：安装本身不依赖控制台，日志文件仍在（见 SilentRunner）。
        }
    }
}
