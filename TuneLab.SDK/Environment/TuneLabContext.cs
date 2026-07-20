namespace TuneLab.SDK;

// ITuneLabContext 的全局访问点。宿主启动时（插件加载之前）注入真实实现；赋值前 / 测试中为 NullContext。
// setter internal：SDK 是全体插件 ALC 共享的同一份，若 setter 公开，任一插件即可 Global = 自己的实现、
// 替换全体插件的语言/日志器（绕过"日志前缀由宿主判定、插件无法伪造"的设计）。宿主经既有 InternalsVisibleTo
// 注入（Program.cs），插件（独立程序集、不在 InternalsVisibleTo 名单）读得到 Global 但改不了。
public static class TuneLabContext
{
    public static ITuneLabContext Global { get; internal set; } = NullContext.Instance;
}

// 空实现：语言空串、no-op 日志器。守住注入前的读取窗口，使插件读 Global 永不为 null。
sealed class NullContext : ITuneLabContext
{
    public static readonly NullContext Instance = new();
    public string Language => string.Empty;
    public ILogger GetLogger() => NullLogger.Instance;

    sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();
        public void Debug(object? value) { }
        public void Info(object? value) { }
        public void Warning(object? value) { }
        public void Error(object? value) { }
    }
}
