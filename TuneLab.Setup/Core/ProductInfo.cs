using System;
using System.IO;
using System.Reflection;

namespace TuneLab.Setup.Core;

/// <summary>
/// 安装器与被装应用的静态身份信息。集中一处便于随发布版本调整。
/// </summary>
internal static class ProductInfo
{
    public const string ProductName = "TuneLab";
    public const string Publisher = "TuneLab";

    /// <summary>被装应用的主可执行文件名。</summary>
    public const string ExecutableName = "TuneLab.exe";

    /// <summary>写入卸载注册表的产品 id（HKCU Uninstall 子键名）。</summary>
    public const string UninstallKeyName = "TuneLab";

    /// <summary>官网 / 帮助链接，展示在"关于/卸载"信息里。</summary>
    public const string HelpUrl = "https://tunelab.app";

    /// <summary>
    /// 默认安装到每用户目录 %LocalAppData%\Programs\TuneLab：
    /// 无需管理员、自更新可直接覆盖、与 Squirrel/Velopack 的约定一致。
    /// </summary>
    public static string DefaultInstallDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            ProductName);

    /// <summary>安装器自身运行时的版本（= 载荷版本，随发布回填）。</summary>
    public static Version Version =>
        typeof(ProductInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// 用于展示的版本串，与打包时 -p:Version 一致（如 "2.0.0"）。
    /// 优先取 AssemblyInformationalVersion（原样保留三段号），否则回退到裁到三段的 Version。
    /// </summary>
    public static string VersionString
    {
        get
        {
            var info = typeof(ProductInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                // 去掉可能的 "+<commit>" 构建元数据
                int plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            return Version.ToString(3);
        }
    }
}
