namespace TuneLab.TestPlugins.Suite.Common;

// 共享基建：被同包 format + voice 插件共用，验证"打一包免重复分发基建 + 同 ALC 只加载一份"。
public static class SuiteCommon
{
    public const string Tag = "v1-suite";
    public static string Label(string who) => $"[{Tag}] {who}";
}
