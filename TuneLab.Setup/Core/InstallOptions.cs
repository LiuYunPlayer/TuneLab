namespace TuneLab.Setup.Core;

/// <summary>向导收集到的用户选择。</summary>
internal sealed class InstallOptions
{
    public string InstallDir { get; set; } = ProductInfo.DefaultInstallDir;
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public bool RegisterFileAssociations { get; set; } = true;
    public bool LaunchAfterInstall { get; set; } = true;

    /// <summary>更新模式：只覆盖文件 + 刷新卸载表版本，不重建快捷方式/文件关联（保留用户原选择）。</summary>
    public bool IsUpdate { get; set; }
}
