namespace TuneLab.Tests;

// 内建（空）音源引擎的一次性加载。
//
// 为什么需要：建 MidiPart 就会构造 SoundSource，而它要向 VoicesManager 求声明 config——那条路以
// 「空引擎」兜底（`GetInitedEngine(string.Empty)!`），空引擎只在内建加载时注册，而无 UI 的测试进程里
// 没人加载过。
//
// 为什么要收成一处并加锁：`VoicesManager.LoadBuiltIn()` 对重复注册本身有防护（同包同 type 会 warn
// 后忽略），但注册表是普通 OrderedMap、**并发调用不安全**。xUnit 并行跑不同 test class，多个类各自
// 在静态构造里调它时，两个线程会同时 TryGetValue 失败、同时 Add，后到的那个抛
// "An item with the same key has already been added"，该类全部用例连带失败。
// 宿主本身没这个问题（启动时单线程调一次），故修在测试侧而不是改宿主。
internal static class TestVoices
{
    static readonly object mLock = new();
    static bool mLoaded;

    public static void EnsureBuiltIn()
    {
        lock (mLock)
        {
            if (mLoaded)
                return;
            TuneLab.Extensions.Voices.VoicesManager.LoadBuiltIn();
            mLoaded = true;
        }
    }
}
