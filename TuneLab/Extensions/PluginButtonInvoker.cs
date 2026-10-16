using System;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Avalonia.Controls;
using TuneLab.I18N;
using TuneLab.SDK;
using HostLog = TuneLab.Foundation.Log;

namespace TuneLab.Extensions;

// ButtonConfig 点击的宿主侧执行（注入 GUI 层的 ButtonActions.Invoke）：在 UI 线程上调用 OnClick，交给它本次点击的
// 弹窗服务（父窗口 = 按钮所在窗口），点击结束即让该服务失效；异常记日志并弹错误提示，标明来源插件
//（按回调所属程序集的加载上下文判定 = 插件包目录名，插件改不了；宿主 / 内置归 "host"）。
// 防重入、执行中置灰、结束后刷新所在面板由 GUI 层 ButtonActions 统一负责。
internal static class PluginButtonInvoker
{
    public static async Task Invoke(ButtonConfig config, Window? owner)
    {
        string source = SourceOf(config);
        var dialogs = new HostDialogService(source, owner);
        try
        {
            await config.OnClick(new ClickContext(dialogs));
        }
        catch (OperationCanceledException) when (HostLifetime.Token.IsCancellationRequested)
        {
            // 应用退出中：取消是正常结局
        }
        catch (Exception ex)
        {
            HostLog.Error("[" + source + "] Button action failed: " + ex);
            await new HostDialogService(source, owner).ShowMessage("Error".Tr(TC.Dialog), ex.Message);
        }
        finally
        {
            dialogs.Invalidate();
        }
    }

    static string SourceOf(ButtonConfig config)
    {
        var assembly = config.OnClick.Method.DeclaringType?.Assembly;
        var name = assembly == null ? null : AssemblyLoadContext.GetLoadContext(assembly)?.Name;
        return string.IsNullOrEmpty(name) || name == "Default" ? "host" : name;
    }

    sealed class ClickContext(IDialogService dialogs) : IButtonClickContext
    {
        public IDialogService Dialogs => dialogs;
    }
}
