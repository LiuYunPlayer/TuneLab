using Xunit;

namespace TuneLab.Tests;

// 真起 sidecar 子进程的测试类共用的串行域：每台机器同时只跑一个 spawn 型测试。
// 并行时十余个 dotnet 子进程同时冷启动会把握手/超时预算挤爆（全量套件里实测抖过）。
// 协议逻辑本身（framing/codec/gate/queue）不碰进程，照常并行。
[CollectionDefinition("SidecarSpawns", DisableParallelization = true)]
public class SidecarSpawnsCollection { }
