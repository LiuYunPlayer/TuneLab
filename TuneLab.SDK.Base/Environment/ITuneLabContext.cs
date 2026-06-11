namespace TuneLab.SDK.Base.Environment;

// 插件读宿主状态 + 取日志器的入口。宿主启动时把唯一实现注入 TuneLabContext.Global，插件经它读取（service-locator）。
// SDK.* 是共享契约程序集（Default ALC 加载一份、所有插件 ALC 共享），故该静态点对宿主与全部插件可见同一实例。
public interface ITuneLabContext
{
    // 当前宿主语言（culture，如 "zh-CN"）。全局量；切语言靠重启生效。
    string Language { get; }

    // 取日志器：前缀由宿主按调用者所属 ALC（= 插件包）自动判定，插件无法伪造。
    ILogger GetLogger();

    // 在自动判定的插件前缀后再加一段子标签。
    ILogger GetLogger(string subName);
}
