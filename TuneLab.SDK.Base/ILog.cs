namespace TuneLab.SDK.Base;

// 插件作用域日志（§三.10）：host 加载插件时注入每插件实例，自动打插件 id 前缀、
// 转发进 host 现有 sink。弃用 static TuneLabContext.Global 式服务定位器
// （ALC 隔离下静态每-ALC 一份，全局共享不了）。
public interface ILog
{
    void Debug(object? value);
    void Info(object? value);
    void Warning(object? value);
    void Error(object? value);
}
