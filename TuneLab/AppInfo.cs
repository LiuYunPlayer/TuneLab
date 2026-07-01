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

    /// <summary>用于展示的版本串（如 "1.6.0"）：优先 AssemblyInformationalVersion（去掉 "+commit"），否则裁到三段。</summary>
    public static string VersionString
    {
        get
        {
            var info = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                int plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            return Version.ToString(3);
        }
    }
}
