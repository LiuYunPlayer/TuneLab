using System;

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

    /// <summary>
    /// 「等 TuneLab 退出」这一步最多等多久。null = 一直等（向导：用户看得见进度、可以点取消）。
    /// 无人值守的路子必须给一个值——那里没人会去关应用，一直等就是让脚本挂死，而且既没有退出码
    /// 也没有一句能读的原因。
    /// </summary>
    public TimeSpan? WaitForAppExitTimeout { get; set; }
}
