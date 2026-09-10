using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace TuneLab.GUI;

// 「此刻有没有东西挡在界面前面」——非人不能答的那种：模态框、系统的文件选择器。
//
// 【为什么非要有这一条】模态框期间 Avalonia 跑的是嵌套消息循环，Dispatcher 照常泵，于是**命令桥照常
// 应答**。外部因此可以在用户屏幕被一个框锁着的时候若无其事地跑一串命令、条条回报成功，而用户一个字都
// 看不见、也点不了——那正是"假装成功"的一种，且是最难被发现的一种（每一条单独看都没错）。
// `editor status` 报了在播吗、拿着哪支笔、焦点在哪个面，唯独没报这一条，那恰恰是最该报的。
//
// 【两路来源，因为它们的可见性不同】
//  · 自家的模态窗不必登记：Avalonia 自己数得出（Window.IsDialog 恰好就是"被 ShowDialog 弹出来的"）。
//  · 系统文件选择器**根本不是 Avalonia 的窗口**，谁也数不到，故只能由调用处显式声明一段作用域
//    （Track）。漏登记一处的后果是那一处期间报"没东西挡着"——所以新增选择器调用要顺手用 Track，
//    别直接调 StorageProvider（TuneLab.GUI 的 PickFiles/PickFolder 与 Editor 里那几处都已经用了）。
internal static class BlockingUi
{
    // 此刻挡着界面的东西，各给一句给人看的说明（模态窗给标题，选择器给用途）。空 = 没有。
    // 必须在 UI 线程上读（要遍历窗口表）。
    public static IReadOnlyList<string> Current()
    {
        var blocking = new List<string>();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (!window.IsDialog || !window.IsVisible)
                    continue;

                var title = window.Title;
                blocking.Add(string.IsNullOrEmpty(title) ? window.GetType().Name : title);
            }
        }

        lock (sLock)
            blocking.AddRange(sSystemDialogs);

        return blocking;
    }

    // 声明"从现在到 Dispose 为止，有一个系统对话框（文件选择器等）挡着界面"。what = 给人看的用途说明。
    // 用 using 包住那次 await：选择器返回时作用域自然结束，用户取消或选完都一样。
    public static IDisposable Track(string what)
    {
        lock (sLock)
            sSystemDialogs.Add(what);
        return new Scope(what);
    }

    sealed class Scope(string what) : IDisposable
    {
        bool mDisposed;

        public void Dispose()
        {
            if (mDisposed)
                return;
            mDisposed = true;
            lock (sLock)
                sSystemDialogs.Remove(what);
        }
    }

    // 同一种选择器可能同时开两个（不该发生，但真发生时按次数进出才不会提前清零），故用列表而不是集合。
    // 登记与读取都在 UI 线程上，加锁只是不指望以后每个调用者都记得这一点。
    static readonly List<string> sSystemDialogs = [];
    static readonly object sLock = new();
}
