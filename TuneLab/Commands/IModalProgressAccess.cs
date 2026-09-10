using System;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Commands;

// 「把一件耗时的活儿跑在一个模态进度框后面」——命令面里唯一会占住用户机器几分钟的那条
//（`project export-audio`）用它。
//
// 【为什么非要有这个框】那条命令的授权卡片写着"期间用户的窗口会被锁住"，用户正是据这句话决定要不要
// 现在把机器交出去的。而渲染跑在后台线程上，UI 线程是空的——没有这个框，那句话就是假话：用户会以为
// 自己该等着，实际却随手就能改工程，而混音是逐块算的，改动落在中途就得到一个前后不同源的文件，
// 回报还说导出成功。界面上那条「导出混音」从来没有这个问题，正是因为它前面挡着同一个框。
//
// 所以这个接口存在的理由不是"给用户看进度"（那是附带的好处），是**让那句承诺成立**。
//
// 有窗口的宿主才给得出；headless 里为 null，命令就地跑——那儿没有窗口可锁，也没有第二双手会去改工程
//（一个进程、一条数据线程、命令顺序执行）。
internal interface IModalProgressAccess
{
    // 在模态框后面跑 work，跑完关框。work 在后台线程上执行（不能占住数据线程，否则框自己都画不出来），
    // 期间用户点不动界面。progress 取值 [0,1]，会 marshal 回 UI 线程更新进度条。
    // work 抛出的异常原样抛回调用方——框先关掉，错误由命令自己按它的口径回报。
    // 框上写什么由实现方决定（那是界面的措辞，且要本地化）——命令面不传文案进来。
    Task<T> RunBehindModalAsync<T>(Func<IProgress<double>, CancellationToken, T> work, CancellationToken cancellationToken);
}
