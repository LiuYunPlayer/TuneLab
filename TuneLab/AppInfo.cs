using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab;

internal class AppInfo
{
    public static Version Version => Assembly.GetEntryAssembly()!.GetName().Version!;
}
