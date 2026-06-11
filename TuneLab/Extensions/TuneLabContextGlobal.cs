using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using TuneLab.I18N;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.Environment;
using HostLog = TuneLab.Foundation.Utils.Log;

namespace TuneLab.Extensions;

// 注入给插件的 ITuneLabContext 唯一实现（宿主启动时装进 TuneLabContext.Global）。
//   Language：取宿主当前语言（实时读，切语言靠重启生效）。
//   GetLogger：按调用者所属 ALC 名（= 插件包目录名，由 PluginLoadContext 设定、插件改不了）自动加前缀，
//              转发进宿主既有 sink。内置 / Default ALC 归 "host"。
internal sealed class TuneLabContextGlobal : ITuneLabContext
{
    public string Language => TranslationManager.CurrentLanguage.Value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public ILogger GetLogger() => new PrefixedLogger(ResolveCallerScope(Assembly.GetCallingAssembly()));

    static string ResolveCallerScope(Assembly caller)
    {
        var name = AssemblyLoadContext.GetLoadContext(caller)?.Name;
        return string.IsNullOrEmpty(name) ? "host" : name;
    }

    sealed class PrefixedLogger(string scope) : ILogger
    {
        public void Debug(object? value) => HostLog.Debug(Format(value));
        public void Info(object? value) => HostLog.Info(Format(value));
        public void Warning(object? value) => HostLog.Warning(Format(value));
        public void Error(object? value) => HostLog.Error(Format(value));
        string Format(object? value) => "[" + scope + "] " + value;
    }
}
