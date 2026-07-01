namespace TuneLab.Setup.Core;

/// <summary>向导收集到的用户选择。</summary>
internal sealed class InstallOptions
{
    public string InstallDir { get; set; } = ProductInfo.DefaultInstallDir;
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public bool RegisterFileAssociations { get; set; } = true;
    public bool LaunchAfterInstall { get; set; } = true;
}
