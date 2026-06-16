using System;
using System.Collections.Generic;
using TuneLab.Configs;

namespace TuneLab.Data.Synthesis;

// 进程级 effect 处理并发闸门：所有 part 的所有「effect×段」处理器共享同一并行预算
// （= Settings.MaxParallelSynthesisTasks，<=0 视为按核数自动）。effect 模型（如 SVC）昂贵，
// 跨段/跨 part 并行须有全局上限避免资源爆。
//
// 线程纪律：全部成员仅数据线程调用（pipeline 调度恒在数据线程）——故纯计数 + 回调队列、无锁。
internal static class EffectTaskGate
{
    // 当前并行上限：设置 <=0 按核数自动；下限 1。
    public static int Limit
    {
        get
        {
            int configured = Settings.MaxParallelSynthesisTasks.Value;
            return configured > 0 ? configured : Math.Max(1, Environment.ProcessorCount);
        }
    }

    // 占一个槽；满则返回 false（调用方应 WaitForSlot 登记、待 Release 重 pump）。
    public static bool TryAcquire()
    {
        if (sActive >= Limit)
            return false;
        sActive++;
        return true;
    }

    // 登记"有空槽时回调我重 pump"（去重）。pipeline 销毁时须 Unregister。
    public static void WaitForSlot(Action pump)
    {
        if (!sWaiters.Contains(pump))
            sWaiters.Add(pump);
    }

    public static void Unregister(Action pump) => sWaiters.Remove(pump);

    // 释放一个槽并唤醒全部等待者（各自重 pump、按需重新 TryAcquire）。一次性清空快照后回调，
    // 避免回调中再登记导致的重入遍历问题。
    public static void Release()
    {
        sActive = Math.Max(0, sActive - 1);
        if (sWaiters.Count == 0)
            return;

        var waiters = sWaiters.ToArray();
        sWaiters.Clear();
        foreach (var waiter in waiters)
            waiter();
    }

    static int sActive;
    static readonly List<Action> sWaiters = new();
}
