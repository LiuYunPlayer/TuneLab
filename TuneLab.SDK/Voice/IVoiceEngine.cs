using TuneLab.Foundation;

namespace TuneLab.SDK;

// 每"引擎类型"一个：加载模型、列声库目录、创建合成会话。
// 有状态插件（跨调用持有昂贵常驻状态，如模型）才有 Init/Destroy；
// Init 是懒调用（首次用到才调），宿主也可主动预热。
public interface IVoiceEngine
{
    // 声库目录（菜单/选择器用，无需创建会话即可读）。属性名与值类型 VoiceSourceInfo 对齐。
    // 契约：必须立即返回、不得阻塞——宿主与 UI 同步读取、无异步等待。实现应在 Init 期间扫描并
    // 缓存声库、get 仅返回缓存引用；惰性加载（首次 get 才扫盘）者自负阻塞 UI 之责。
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; }

    // 无参、失败抛异常：宿主在调用边界 catch，责任归属靠捕获点判定（从插件调用边界出来的就是插件侧责任）。
    // 不传安装路径——插件 DLL 经 Assembly.Location 即可自定位包目录。
    void Init();
    void Destroy();

    // voiceId 选定声库（VoiceSourceInfos 的 key）；context 为该 part 的输入活视图，随会话同生共死。
    ISynthesisSession CreateSession(string voiceId, ISynthesisContext context);
}
