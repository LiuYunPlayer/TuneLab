namespace TuneLab.SDK.Base.Environment;

// ITuneLabContext 的全局访问点。宿主启动时（插件加载之前）注入真实实现；赋值前 / 测试中为 NullContext。
public static class TuneLabContext
{
    public static ITuneLabContext Global { get; set; } = NullContext.Instance;
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
