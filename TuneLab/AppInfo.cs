using System;
using System.Reflection;

namespace TuneLab;

internal class AppInfo
{
    public static Version Version => Assembly.GetEntryAssembly()!.GetName().Version!;
}
