# 脚本插件 Sidecar 系统设计（Python / Node.js / Lua）

> 状态：**定稿待评审**。基线 = fork `master`（2.1.1，`cfa9fff7`），分支 `feat/scripted-extensions`。
> 配套阅读：[plugin-development.md](plugin-development.md)（V1 插件契约）、[sdk-api-evolution.md](sdk-api-evolution.md)（冻结 ABI 纪律）、[naming-glossary.md](naming-glossary.md)。
> 本文只设计**宿主侧与协议侧**；`TuneLab.SDK` / `TuneLab.Foundation` 一行不动（§1.3）。

---

## 0. 定位与非目标

脚本 sidecar 体系把 Python / Node.js / Lua 插件接入 V1 扩展系统：脚本插件与 .NET 插件共用同一
manifest 体系、注册表、路由、设置、侧栏 UI、`.tlx` 打包安装管线；**唯一区别是实现源在进程外**。

- 路线 = 跨进程 sidecar（JSON-RPC over stdio + 共享内存数据面）。**不开第二路径**：Lua 虽有干净的
  进程内嵌入选项（KeraLua/NLua），一旦并立，"一份协议、每语言一薄层 façade"的核心优势即崩解；
  且三语言的 native 扩展（torch / node addon / cjson）都能 segfault，崩溃隔离对 Lua 同样成立。
- **非目标**：
  - `instrument`（实时音源）不进本期——实时回调 + 低延迟音频环是另一道题，明确不承诺。
  - 不承诺 win-x86 的 Python/Node 支持（官方产物就没有）；平台矩阵**按 runtime 实际能力声明**（§8.5），
    用户可自带编译版扩格（§8.6）。
  - 宿主代跑 `pip/npm install` 不做（依赖策略 = 作者 vendoring，§12.2 预留字段除外）。
- 既有事实锚点（本设计全部复用，零改动）：注册表不关心实现来源（`ExtensionManager.cs:546 RegisterEntry`
  只验证"接口 + 无参 ctor"）；路由键 `kind:identity`（`ExtensionRouting.cs:48`）；设置按
  `packageId → kind:engineId` 分桶；资源包=不加载代码的 type 注册（`ExtensionManager.cs:240`）；
  热安装（`Editor.cs:1229 InstallExtensions`）、卸载延迟交 `ExtensionInstaller`；
  Legacy 单向适配器的挂点先例（`ExtensionManager.cs:68 LegacyLoadHook`）。

---

## 1. 总览

### 1.1 一条脚本插件的解剖

```
%APPDATA%\TuneLab\Extensions\com.example.pyvoice\     ← .tlx 解包后的包目录（与 .NET 插件同级）
   manifest.json      ← 声明 runtime + entry（§2）
   plugin\main.py      ← 入口模块（宿主 spawn runtime 后 import/run 它）
   vendor\             ← （可选）作者 vendoring 的第三方库
   Introduction.md
        │ 宿主侧
        ▼
ScriptedLoadHook ──探测 runtime──▶ SidecarHost（package 粒度一个子进程）
        │           manifest.json    stdio: JSON-RPC 控制面
        │                            映射区:   共享内存音频/曲线面
        ▼
RemoteFormat / RemoteEffectEngine / RemoteVoiceEngine : SDK A 类接口的 RPC 转发代理
        ▼
现有 FormatsManager / EffectManager / VoicesManager / ExtensionSettingsManager / 路由 / 侧栏 —— 零改动
```

### 1.2 三条版本轴（各管各的，勿对齐）

| 轴 | 位置 | 管什么 | 现值 |
|---|---|---|---|
| `AssemblyVersion` | SDK/Foundation csproj | .NET 绑定层，钉死不动 | `2.0.0.0` |
| `SdkVersion` | `ExtensionManager.cs:29` | .NET 插件编译面 vs 宿主（manifest `sdk-version` 门，`ExtensionManager.cs:185`） | `1.1` |
| **`ProtocolVersion`（新）** | `Scripted\WireProtocol.cs` | wire 协议帧/方法表演进，宿主与 façade 双台账 | `1` |

脚本条目 manifest 仍必填 `sdk-version`：它经握手映射成 façade 支持的 SDK 面区间（façade 声明
`tunelab_sdk = "1.1"`），校验语义与 `sdk-version` 门同构——"脚本插件用了新面 × 旧宿主/façade"在握手期
人话拒绝，不进运行时。

### 1.3 冻结 ABI 边界

新增的一切都是宿主 internal（`TuneLab\Extensions\Scripted\`）或脚本侧包。SDK 的 A 类接口由
`Remote*` 代理实现（宿主消费方向不变）；B 类宿主实现面**不出进程**——sidecar 看到的是它们在协议侧的
镜像（§5.3），插件永远拿不到活的 .NET 对象。`PublicAPI.*.txt` 全程不动。

---

## 2. manifest 扩展

### 2.1 新字段（宿主侧 DTO：`ExtensionManifest.cs` / `ExtensionInfo.cs`，非 SDK）

条目级（entry）：

| 字段 | 要求 | 语义 |
|---|---|---|
| `runtime` | 声明即成脚本条目 | 运行时区间串：`"python>=3.10,<4"` / `"node>=18"` / `"lua>=5.4"` / `"luajit>=2.1"`（flavor 前缀参与判定，§8.3） |
| `entry` | 与 `runtime` 成对 | 包内相对路径：`plugin/main.py` / `plugin/index.js` / `plugin/main.lua` |
| `deps` | 选填 | `"vendored"`（默认，隐式）/ `"pip"`（**已声明未实现**：见到即 `Skipped: deps mode not supported`） |
| `protocol-version` | 包级，选填 | 缺省按 1；高于宿主 `WireProtocol.Version` → 握手前 `Skipped`（与 `sdk-version` 门同款文案） |

规则（全部是加载错误，不静默降级——延续 V1 纪律）：

- 脚本条目**不得**再写 `class`/`assembly`；`runtime` 与 `entry` 必成对；与 `class/assembly` 混写 = 错误。
- 判别次序：manifest 含 `id` ⇒ V1（不变）；V1 条目含 `runtime` ⇒ 分流 `ScriptedLoadHook`，否则走现有
  ALC 路径。含 `runtime` 但 `id` 缺失 = Legacy 时代不存在此字段 ⇒ 仍按 Legacy 处理（其 `runtime` 被忽略），
  文案提示应补 `id`。
- 脚本条目同样走包级/条目禁用门、`platforms` 门、`sdk-version` 门（`ExtensionManager.cs:140` LoadV1 内、
  ALC 之前分流，位置在 `!IsCodeKind` 与 `ValidateSdkReferences` 之间，`ExtensionManager.cs:240/281`）。
  连带一处必改：未知 `type` 的"资源包不声明代码"判据（`ExtensionManager.cs:246`，现只认
  `assembly/class`）须扩成也认 `runtime/entry`——否则 `type` 拼错的脚本条目会被当资源包静默登记成
  loaded，正是该处注释点名要防的"比报错更误导"。
- `type:"runtime"` 是**新资源类型**（§8.4）：零代码，走现有资源注册路径；不填 `assembly/class`，
  新增包级字段 `flavor`（`python|node|lua|luajit`）、`runtime-version`（精确串，如 `"3.12.7"`）。
  老宿主见到未知 `type` 本就当资源登记，新宿主才消费它的 `flavor`——天然向后兼容。

### 2.2 脚本插件条目示例（voice，单条目简写形）

```json
{
  "id": "com.example.pyvoice", "name": "Py Voice", "version": "1.0.0",
  "sdk-version": "1.1", "protocol-version": 1,
  "runtime": "python>=3.10,<4", "entry": "plugin/main.py",
  "type": "voice", "engine": "PyVoice",
  "introduction": "plugin/Introduction.md",
  "localizations": { "zh-CN": { "name": "示例语音引擎", "introduction": "plugin/Introduction.zh-CN.md" } },
  "platforms": ["win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64"]
}
```

### 2.3 加载期校验（握手后）

sidecar 在 `initialize` 应答里回 `provides: [{ "type", "engine"|"suffixes", "settings": bool }]`，宿主逐条
核对 manifest 承诺：缺席 = `Failed: entry did not declare <kind>:<id>`；多余同理想声明才有效。
`IExtensionSettings` 探测从"静态查类型"（`ExtensionManager.cs:611`）换成握手字段 `settings`。

---

## 3. 进程与生命周期

### 3.1 归属与粒度

- **一个 sidecar = 一个包**（不是条目、不是会话）。多 part / 多 effect 实例的 session 在一条 stdio 上
  多路复用；模型只随进程加载一次。
- `ScriptedRegistry`（新，宿主 internal 单例）持有 `packageId → SidecarHost`；`Remote*` 代理从它取通道。
- **懒起**：`Init` 首次被需要时 spawn（对齐 .NET 引擎的懒 Init；App 启动的 eager `InitEngine` 预热对脚本
  引擎默认关闭，设置可开——避免开机拉一堆 torch）。

### 3.2 状态机

```
None → ResolvingRuntime → Spawning → Handshake → Ready
                            │  (EOF / 退出 / 握手失败)              │ IO/心跳超时
                            ▼                                     ▼
                        Restarting(backoff) ◀───────────────── Dying(收割)
                            │ 窗口内 ≥5 次重启仍死
                            ▼
                        Failed(circuit open)   ——侧栏可见，含 stderr 尾部摘要
```

- **epoch**：每次 spawn 递增。宿主对每个 session 记录其 epoch；一切 sidecar→宿主 的 publication /
  RPC 应答带 epoch，旧 epoch 到达即丢（防"复活后旧进程尾包打进新会话"）。崩溃=隐式取消在途请求
  （对齐"取消是正常结局"语义），产物保留上一版。
- **退避**：1s/2s/5s/10s/30s；窗口（10 min）内重启 ≥5 → 熔断 `Failed`，用户点"重新加载"或重启宿主复位。
- **正常关机**：`ExtensionManager.Destroy`（`App.axaml.cs` desktop.Exit 路径）→ 先对全部 sidecar 发
  `shutdown`（限时 3s）→ 未退则 Kill → 再走现有 .NET 引擎 Destroy。卸载因此不再被 sidecar 文件锁卡住。

### 3.3 防遗留四防线（按优先级；1/2/3 冗余并存）

| # | 机制 | 平台 | 覆盖 |
|---|---|---|---|
| 1 | **stdio EOF 自杀**：façade 用专用线程阻塞读 stdin，EOF → 先收割自己的子进程（dataloader/child）再退 | 全平台 | 一切进程死亡（含 `Program.cs:79` 的 `Kill()`——该路径不跑托管清理，OS 关句柄仍然成立） |
| 2 | **OS 父死钩子**：Windows Job Object（`KILL_ON_JOB_CLOSE`+禁 breakaway，子进程自动继承）；Linux `prctl(PR_SET_PDEATHSIG,SIGTERM)`；macOS kqueue `NOTE_EXIT` 挂父 pid | 分平台 | 子进程阻塞在 native 长调用、看不见 EOF 的死角 |
| 3 | **心跳**：控制面 ping 15s / 超时 60s。超时 ≠ 自杀——宿主挂起（进程活着）时 sidecar **不**退出（丢模型重载是白丢）；心跳只用于宿主识别"半死 sidecar"→ 判死重启 | 全平台 | 卡死的 sidecar |
| 4 | **启动清扫**：spawn 时把 `{pid, create-time, packageId, nonce}` 记 `Configs\scripted-pids.json`；下一实例持锁（`TuneLab.lock`）后清扫——按 pid+进程创建时间+可执行路径三重匹配防 PID 复用，只杀路径落在 Extensions 下的残留 | 全平台 | BSOD/断电后的开机残留 |

### 3.4 spawn 规范

- argv：`<runtime-exe> [语言旗标] <entry绝对路径>`（python：`-u` 禁缓冲；node：`--disable-warning=ExperimentalWarning`；lua/luajit：裸跑）。
- stdin/stdout **只归协议**；日志一律走控制面 `log.write` 通知（§5.4），由宿主转发进既有 `ILogger`
  （按 ALC 归因的等价物 = packageId 归因）。stderr 捕获到包目录 `logs/stderr-<boot>.txt`（滚动 2 份），
  尾部 64KB 供 `Failed` 文案。**禁止**插件往 stdout 打裸文本（帧即坏，判死重启——文档写明）。
- 环境变量注入：`TUNELAB_PROTOCOL=1`、`TUNELAB_EPOCH`、`TUNELAB_NONCE`（shm 命名域，§7）；置
  `PYTHONUNBUFFERED`/`NODE_OPTIONS` 白名单式控制，其余继承。
- Windows：`CreateNoWindow` + 非重叠 stdio 句柄；隐藏控制台差异封装在 `ScriptedLauncher` 一处。

### 3.5 数据线程纪律（协议级硬约束）

宿主数据线程上**永不**做无界同步 RPC。规则：

- 声明面调用（`get_*_configs`，宿主在参数 commit 时数据线程要结果）→ **宿主侧缓存**：值按
  `(engineId, 参数快照)` 键缓存；miss 时起异步 RPC + 超时（默认 200ms）→ 超时则本拍用旧缓存/返回上值，
  应答到达后按 commit 流程 diff 到 UI。UI 声明位可容忍一拍延迟，正确性不受影响（声明本就是纯函数）。
- `get_next_pending_synthesis_range`（peek）→ 同上"异步答 + 缓存"：sidecar 靠 §5.5 推送的变更事件自维护
  增量脏表，peek 是它内存状态的本地查询（无回调）；宿主对 peek 结果按 `(session, window, 变更序号)` 缓存，
  miss 即等下一次调度拍（不阻塞数据线程）。
- 其余（synthesize/process/声明/init…）全在异步续体上，超时按方法配置（synthesize 不设 RPC 超时，
  靠取消）。

---

## 4. 自恢复：崩溃 = 会话重建

既有语义背书：tempo 变化/part 位移时宿主本就 dispose 旧会话、带新 context 全量重建、插件从零重合成
（plugin-development §5.9）。**sidecar 崩溃恢复 = 重放该路径**，插件作者不写任何恢复代码：

| 类型 | 死亡期表现 | 恢复动作 |
|---|---|---|
| format | 当次导入/导出报错（用户可见，人话） | 无状态，下次调用即新进程 |
| effect | **passthrough**（`EffectManager.GetInitedEngine` 失败即降级的现成通路） | 重 Init + 重建全部 processor；宿主保守调度自动重跑 `Process` |
| voice | 状态带保上一版 + `Synthesizing…restarting` 提示 | 重 Init + 重建全部 session（epoch++）；调度器自然重合成脏/可见区 |
| settings | 面板暂用缓存 schema | 重拉 `get_settings_config`，桶数据在宿主侧不受影响 |

## 5. RPC 协议

### 5.1 帧与消息

- 帧 = LSP 式头段 `Content-Length: <n>\r\n\r\n` + UTF-8 JSON body（单写线程聚批，防管道碎帧）。
- JSON-RPC 2.0 子集：`request(id,method,params)` / `reply(id,result|error)` / `notification(method,params)`。
- 方法名 snake_case；错误对象 `{code, message}`，code 枚举稳定：`bad_request, unsupported_protocol,
  stale_epoch, stale_handle, runtime_error, cancelled, timeout, io`。

### 5.2 系统方法（双向）

| 方法 | 方向 | 语义 |
|---|---|---|
| `initialize {protocol, sdk_version, package_id, entries[]}` → `{provides[], capabilities{audio_plane,curve_plane}, runtime{exe,version,arch,flavor}, facade_version}` | H→S / 应答 | 握手（§3.4 环境变量核对 epoch/protocol；不符即关管道） |
| `log.write {level, message}` | S→H n | 插件日志 → 宿主 ILogger（packageId 归因） |
| `ping {}/pong {epoch}` | 双向 n | 心跳（§3.3-3） |
| `shutdown {why}` | H→S c | 礼貌退出（§3.2） |
| `capabilities.report {…}` | S→H n | 运行中能力变更（罕见，v2 预留） |

### 5.3 载荷编解码约定（"句柄化宿主实现面"）

B 类接口**不逐项远程化**。判据：sidecar 只需要 (a) 一次性物化的值（快照、属性对象）、(b) 批量求值
（曲线面）、(c) 就地写（音频面）。因此：

- 值对象：`PropertyObject` / `ObjectConfig` / 快照族 / `Synthesized*` → JSON（字段名 = SDK 属性名 camelCase；
  `NaN` 用字符串 `"nan"`——JSON 不合法 NaN 的机械修）。
- 句柄：`snapshot_id`（随会话 epoch 失效）、`evaluator` = `(snapshot_id, track_key)` 复合、
  `segment_id`（宿主 `IAudioSegment` 握柄的镜像；`Create/Write/Commit/Resize/Dispose` 一一对应）。
- 活视图（context/note/automation 的通知流）→ **不镜像**，改为 §5.5 的推送事件流 + 快照拉取。
  这是全协议最大的一条简化：跨进程没有"随手读活的宿主对象"，一切经事件与快照——正与 V1 自己的
  线程纪律同构（worker 只读快照）。

### 5.4 format（Phase 1）

| 方法 | 参数 → 结果 |
|---|---|
| `format.import {entry_id, stream_ref} → {project_info}` | 宿主把源文件 copy 到暂存 `<AppData>\TuneLab\Temp\io-<nonce>\<id>.in` 传路径；façade 以二进制流读，回 ProjectInfo JSON |
| `format.export {entry_id, project_info, stream_ref} → {}` | sidecar 写暂存 out 文件；宿主读进既有的 MemoryStream 原子写流程（`FormatsManager` 语义不变），失败=导出错误 |

ProjectInfo/TrackInfo/…/NoteInfo 全族 DTO 已是值对象，JSON 映射表进 façade 文档（字段逐一对齐，
宿主不猜）。**字节流不进消息体**（大工程导出可数十 MB——文件交接零拷贝）。

### 5.5 引擎与会话方法（effect Phase 2 / voice Phase 3）

引擎级（H→S）：`engine.init {engine_id}`（异步长任务：进度经 `engine.init_progress` n；超时=无，
失败回 error）、`engine.destroy`、`engine.create_session {engine_id, session_id, voice_id|绑定信息}`、
声明五件套 `engine.get_automation_configs {…}` 等（结果经 §3.5 缓存）。

会话推送流（H→S n，voice）：`sess.note_added/note_removed/note_modified{note_id, fields[], …}`、
`sess.properties_changed{keys}`、`sess.automation_range_changed{track, start, end}`、`sess.committed{}`。
note_id = `IVoiceSynthesisNote.Id`（宿主发号，冻结面已按 Id 键控产物，`VoiceSynthesisNoteSnapshot.cs:17`）。

会话级（H→S c）：`sess.get_snapshot {session_id, note_ids[]}` → 快照 JSON（automation/pitch 全量冻结、
求值器以句柄出现——"实现范围只能等音素定时才知道，同步前缀无处确圈窗"的上游结论）；
`sess.peek {session_id, start, end}` → `SynthesisRange|null`（§3.5 缓存通道）；
`sess.synthesize_next {session_id, start, end}`（异步，取消经 `sess.cancel` n）；
产物回传（S→H）：`sess.publish_pitch/parameters/phonemes/status {…}` + 对应 changed 信号
（`StatusChanged` 等四族，`IVoiceSynthesisSession.cs:112-116`）；音频面见 §6.2。

effect 同族：`effect.create_session {context绑定: 输入段几何+参数快照}`、`effect.process`、
`effect.input_read {offset,count} → base64`（或 §6.2 shm 读视图，宿主输入段映射为只读区）、
`Input.RangeModified` → `effect.input_range_changed` n。

### 5.6 曲线面（**已定稿：远程句柄 (a) + shm 兜底**）

- `evaluator.evaluate {snapshot_id, track, positions_b64} → {results_b64}`：小批量 inline
  little-endian f64（阈值 64KB）。
- `evaluator.evaluate {snapshot_id, track, positions_count, out_ref} → {shm_offset}`：大批量走
  `audio_plane: shm` 同族的结果缓冲，façade 以 `numpy`/`Float64Array` 视图取回。
- **宿主在 RPC 工作线程直接出值**——冻结求值器本就不可变、跨线程安全（V1 契约原文），无需 marshal
  回数据线程；这是 (a) 成本可控的根基。插值/投影算法**唯一实现留在宿主**，façade 零移植、零漂移。
- `positions` 非降序为硬前提：façade 排序断言，宿主不校验（乱序=未定义值，同 in-proc 语义）。
- 句柄随 snapshot 换代失效：新 `get_snapshot` 或 `sess.dispose` 即 invalidate 旧 `snapshot_id`，
  命中旧句柄回 `stale_handle`。

### 5.7 settings

`settings.get_config {engine_key}` → `ObjectConfig` JSON；`settings.apply {engine_key, values}`。
宿主侧桶（`ExtensionSettingsStore`、DPAPI/Keychain 密码字段）**不动**：sidecar 永远收不到密码字段的
密文之外形态，schema 里 `IsPassword` 字段值由宿主侧解出后才推给 `settings.apply`（与 .NET 插件同权）。

---

## 6. 传输：三面分道

| 面 | 载荷 | 通道 | 预算 |
|---|---|---|---|
| 控制面 | RPC/事件/快照 | stdio JSON，§5.1 | 每块合成几次往返，µs~ms，忽略 |
| 音频面 | PCM f32（≈384KB/s/流） | **共享内存段**（§7），仅当 `audio_plane: shm` | 零序列化；写=内存带宽 |
| 曲线面 | f64 批量数组 | base64 inline <64KB，超出走 shm 结果缓冲 | µs~ms |

- **音频面镜像 in-proc 语义**：`create_audio_segment {segment_id, sample_offset, count, rate}` →
  `{region_ref}`；façade 映射后给 numpy/TypedArray 视图，`write(offset, array)` 直写宿主缓冲；
  `commit`/`resize`/`dispose` = 16 字节级控制消息。`IAudioSegment.Resize` 的身份保持语义（下游缓存存活）
  原样映射为 `resize` 消息。
- 能力协商：`audio_plane: shm | inline`。`inline` 面 = `segment.write_b64`（PUC Lua、异常平台的兜底；
  协议同构，性能自降，文档如实标注）。
- 明确**不引入** FlatBuffers/CAPNProto/gRPC（schema 工具链在多语言侧的摩擦远大于收益；MessagePack 留作
  benchmark 真要求时的平替位）。TCP/命名管道不做（端口、防火墙、半关语义全是净亏）。

## 7. 共享内存布局与清理

- 区域命名：`tlx-<boot-nonce>-<package-slug>-<seq>`。`boot-nonce` = 宿主本次启动随机串（一切资源随它
  成族；上次崩溃的残留可识别、可清扫）。
- 宿主分配：`MemoryMappedFile`（.NET 跨平台）。POSIX：façade `shm_open` 后**立即 unlink**（fd 已映射，
  双方进程死即净）；Windows：mapping 名进 Global\ 前缀需提权 ⇒ 一律用**文件后备**命名（宿主暂存目录下
  `regions\<name>`），随包卸载/启动清扫删除，进程句柄自动回收。
- 段区头部：`{magic u32, version u16, flags u16, sample_offset i64, sample_count i32, sample_rate i32}`
  后接交织立体声 f32 体；几何变更 = 新段新区域（`Resize` 原地改 header+可扩体区）。
- 生命周期与 `IAudioSegment` 一致：Dispose/换代即宿主解除映射、façade 收 `segment.gone` n 解视图；
  进程死全部由 OS/清扫兜底（§3.3）。
- 曲线面结果缓冲复用同一区域分配器（环形、按 `(epoch, seq)` 命名）。

## 8. 运行时交付与探测

### 8.1 四级探测（RuntimeLocator，加载期完成，不跑插件代码）

1. **设置显式路径**（设置→扩展→运行时；每语言一栏，`PathPickerConfig`——SDK 1.1 现成面）。
2. **环境变量** `TUNELAB_PYTHON` / `TUNELAB_NODE` / `TUNELAB_LUA`（分号多候选；CI/高级用户）。
3. **runtime 资源包**（§8.4）：扫 Extensions 下 `type:"runtime"`、`flavor` 对得上、`platforms` 含当前 RID
   的包，取满足区间的最高版。
4. **系统 PATH**：Python 先 `py -3` launcher 再 `python`/`python3`；Node `node`；Lua `lua`/`luajit`。

### 8.2 真跑核验（每一格都要过）

候选以固定参数起进程一次，读版本+架构（`sys.version`/`platform.machine()`；`process.arch`；
`_VERSION` + `jit` 全局），超时 2s；结果按 `(exe路径, mtime)` 缓存。**防 spoof 硬要求**：
Windows `WindowsApps\python.exe` 商店跳转桩、nvm/fnm shim——跑不过核验即换下一候选。
版本/arch 不满足条目区间 ⇒ `Skipped: requires python>=3.10, found 3.8`（人话，同 `sdk-version` 门文案风格）。

### 8.3 flavor 语义

`runtime` 串 = flavor + 区间。flavor 不匹配（如装了 Lua 5.4 而条目要 `luajit`）视同未找到。
方言碎片化被协议吸收（字节级 JSON over stdio 与方言无关）；façade 声明各方言下限（纯 Lua 无 C 依赖）。

### 8.4 runtime 资源包

```json
{ "id": "com.tunelab.runtime.win-x64.python", "name": "TuneLab Python Runtime",
  "type": "runtime", "flavor": "python", "runtime-version": "3.12.7",
  "platforms": ["win-x64"], "version": "1.0.0" }
```

内含 `runtime\`（python embeddable 树 / node dist 树 / lua bin）。官方发布经
`tools/build-runtime-package/`（抓 python.org embeddable zip、nodejs.org dist、Lua/LuaJIT 构建产物 →
组装 → 打 `.tlx` → sha256 随 GitHub release；`upload-release.yml` 加制品）。

### 8.5 平台矩阵（首年官方格；⚠=社区构建作者自证，❌=官方无产物）

| | win-x64 | win-arm64 | osx-x64 | osx-arm64 | linux-x64 | linux-arm64 | win-x86 |
|---|---|---|---|---|---|---|---|
| Python | ✅ 3.12+ | ✅ 3.14+/uv 3.12+ | ✅ | ✅ | ✅ glibc≥2.28 | ✅ | ❌（3.13+ 停发） |
| Node | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌（v13 起无） |
| Lua | ✅ | ⚠ | ✅ | ✅ | ✅ | ✅ | **✅（C89 可便携，唯一填格者）** |

宿主只按 RID 探测核验；格子由 runtime 官方产物决定，**政策与协议不因语言变**。

### 8.6 缺失时（**定稿：半自动**）

侧栏 `Skipped · 未找到 Python 运行时` + 两按钮：【下载运行时】（走 `AppUpdateManager` 同款
GitHub release + sha256 + 进度条通道，用户明示同意；落地即 `Editor.InstallExtensions` 热装，**无任何
新安装路径**）与【选择已装位置】。设置项"自动获取脚本运行时"默认**关**（开了=免提示，其余同）。
镜像/离线：源 URL 可覆写（与 §8.1-① 的自带编译版是同一条灵活政策的两个面）。
macOS：runtime 资源包安装钩子负责剥 `com.apple.quarantine`；下载的包不进 .app，公证面不变。

### 8.7 并存与回收

多版本 runtime 包并存（目录名带 flavor+版本，探测按区间择优；两插件钉不同版本各得其所）。
被引用中的 runtime 包禁卸（侧栏点名使用方）；提供"清理未被引用的运行时"。它就是普通扩展包——
`ExtensionActivation`/`Installer` 管线零改动。

## 9. façade 与宿主适配层

### 9.1 宿主侧（`TuneLab\Extensions\Scripted\`，全 internal）

`ScriptedLoadHook`（挂 `LoadV1`，模式对齐 `LegacyLoadHook`）→ `RuntimeLocator` → `SidecarHost`
（进程/帧/句柄表/epoch/重启）→ `WireRpc`（§5）→ `ShmChannel`（§7）→
`RemoteFormat(:IImportFormat,IExportFormat)` / `RemoteEffectEngine` / `RemoteVoiceEngine` /
`RemoteScriptedSettings` —— 注册进现有 manager。**这些代理是 B 类的"假象制造者"**：对 manager 而言
它们与 in-proc 引擎不可分辨。

### 9.2 插件侧（新顶层目录 `scripting/`）

| 包 | 交付 | 依赖底线 | 关键点 |
|---|---|---|---|
| `tunelab` (PyPI) | `scripting/python/tunelab/` | **纯 stdlib**（json/threading/`multiprocessing.shared_memory`/ctypes 设 PDEATHSIG） | 装饰器注册 `@tunelab.voice_engine`；`synthesize_next` 是协程，宿主发布经内部 marshal 线程；numpy 数组直写 shm 视图 |
| `@tunelab/plugin` (npm) | `scripting/node/tunelab-plugin/` | TS-first；mmap 走可选 peerDep `koffi`，无则自报 `inline` | 事件循环单线程 ↔ 协议天然契合；CPU 密集 offload 到 `worker_threads`（façade 提供，禁阻塞 loop 饿死心跳） |
| `tunelab` (luarocks / 单文件) | `scripting/lua/tunelab/` | **纯 Lua 单文件**（内嵌 json.lua + 协程 RPC 主循环）；LuaJIT+FFI 才开 `shm` 能力 | PUC 自报 `inline`；曲线经打包 double 串 + `string.unpack` |

façade 内嵌 `wire=1` + `tunelab_sdk` 区间；版本随宿主台账发布，离线 wheel/tar 附在 runtime 资源包内
（vendoring 场景作者本地取用）。SDK 面成员映射表（snake_case ↔ `IVoiceSynthesis*` 逐成员）是 façade
文档的核心附录，漂移即测试红（§12.1 M2 的映射封条）。

## 10. 失败 / 降级矩阵

| 事件 | format | effect | voice | 设置面板 | 用户所见 |
|---|---|---|---|---|---|
| runtime 未找到 | — | — | — | — | `Skipped`+下载按钮（§8.6） |
| 握手协议不符 | 同左 | 同左 | 同左 | 同左 | `Skipped: wire 2 > host 1` |
| sidecar 崩溃 | 当次报错 | passthrough → 恢复重跑 | 上一版产物+重建重合成 | 缓存 schema | `Synthesizing…restarting` / stderr 摘要 |
| sidecar 挂起 | 超时→报错 | 心跳判死→重 spawn | peek/声明走缓存；synthesize 取消→重排 | — | 状态带提示 |
| 宿主猝死 | —（sidecar EOF 自杀，§3.3） | 同左 | 同左 | 同左 | 下次启动清扫 |
| init 抛错 | n/a | 引擎 `Failed` | 引擎 `Failed` | — | 侧栏 `Failed`+原因 |
| stale_handle/尾包 | — | 丢弃 | 丢弃 | — | 无感（epoch 兜底） |

## 11. 安全与信任

- 信任模型与 .NET 插件**完全同级**：扩展即全权代码（vendored 依赖同理），sidecar 不带来新权限面，
  也**不承诺沙箱**。区别只在崩溃隔离与可卸载性。
- 密码字段：密文/明文都不出宿主进程；`settings.apply` 推的是解出的终值（与 .NET 插件同权，§5.7）。
- 下载链：GitHub release 固定 + **sha256 钉死** + 镜像覆写要求同样校验；`tools/build-runtime-package`
  的产物清单进发布检查单。
- `ValidateSdkReferences`（`ExtensionManager.cs:337`）对脚本源不适用——补偿 = 握手期 `sdk-version`
  区间 + façade 映射封条测试（§12）。

## 12. 实施分期与验收

### 12.0 里程碑

| M | 交付 | 验收 |
|---|---|---|
| M0 | 设计文档（本文）+ `runtime/entry/deps` manifest 字段 + `RuntimeLocator` 四级探测（含核验缓存）+ runtime 资源包格式 + `tools/build-runtime-package` + `Skipped` 文案链 | 新用例：字段校验矩阵/探测顺序/spoof 候选跳过；无 runtime 时插件停在 Skipped；编译面不碰任何解释器真链 |
| M1 | `SidecarHost`+`WireRpc`+帧编解码+§3.3 防线 1/2/4+epoch/退避 + **.NET 假 sidecar**（测试工程内子进程，CI 免装解释器）+ `RemoteFormat` + Python `tunelab` + `tests/plugins/PyFormat` 样例 + 【下载运行时】按钮 | 假 sidecar 协议单测全绿；`HeadlessHost`（`TuneLab\Headless\HeadlessHost.cs`）E2E：装 .tlx → 导入导出 → 卸载；杀宿主 → 子进程 ≤2s 自清 |
| M2 | Node/Lua façade + effect（passthrough、心跳、能力协商：PUC Lua 走 inline 验证兜底路）+ stderr 日志收割 + `[重新加载]` | 三语言 effect 样例全跑；崩溃→恢复→重合成用例；映射封条测试 |
| M3 | `ShmChannel` + voice（推送流/会话重建恢复/曲线面 (a)+shm 兜底）+ Python voice 样例（附录 A）+ CI 三平台矩阵（setup-python/setup-node/lua 预编译） | 附录 A 剧本端到端；挂起注入用例（数据线程不冻结的看门狗断言）；合成产物与 .NET 样例逐字段对拍 |

### 12.1 测试资产

`tests/plugins/{PyFormat,NodeFormat,LuaFormat}/` = manifest+源码，无 csproj，直接进 `tests/packages`
被 `pack-tlx.ps1` 收走（绕开三件套的 build 步）；`manifest-variants` 补脚本条目变异体；
`tests/TuneLab.Tests/Scripted/*.cs` 用假 sidecar；E2E 标 `[Trait("runtime","needs-interpreter")]`
CI 专属。协议封条：方法表 JSON schema 一份（`scripting/wire/v1.json`），三语言 façade 各自跑同一回放。

### 12.2 配置项清单（宿主设置，`SettingsRegistry`）

```
scripted.autoFetchRuntime      bool   = false   # §8.6 全自动档开关
scripted.runtimes.python       string = ""      # ①级路径（PathPicker）
scripted.runtimes.node         string = ""
scripted.runtimes.lua          string = ""
scripted.runtimeSource         string = ""      # 镜像 URL 覆写（sha256 仍强制）
scripted.eagerWarmup           bool   = false   # App 启动预热脚本引擎
scripted.initTimeoutSec        int    = 0       # 0 = 无上限（§5.5 长任务语义）
```

开发工效（M1 附带）：`PathManager` 的 `TuneLab.lock`/Extensions 根目录接受环境变量覆盖
（`TUNELAB_DATAROOT`），让 fork 联调实例与日常实例的 `%APPDATA%\TuneLab` 物理隔离——两检出共享
用户目录是已知互踩点（单实例锁 + dll 锁）。

### 12.3 文档与术语

- 用户向：`docs/plugin-development-scripting.md`（英文源 + zh-CN，M1 起随能力增补）；
  `plugin-development.md` 增一节"脚本插件"指针。
- `naming-glossary.md` 已随行增补（sidecar / runtime 资源包 / epoch / 三面 / 会话重建）。

---

## 附录 A：PyVoice 端到端样例（M3 验收剧本）

### A.1 包与 manifest

见 §2.2。布局：`src/{manifest.json, plugin/{main.py, engine.py, g2p.py, acoustic.py},
vendor/(pip install --target 产物), Introduction*.md}`；模型走独立资源包或设置指路径（plugin-development
§5.10 原约定直接沿用）；`platforms` 逐格声明。

### A.2 代码骨架（façade 草样 API）

```python
# main.py
import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "vendor"))
import tunelab
from .engine import PyVoiceEngine

@tunelab.voice_engine                       # 注册；握手时随 provides 上报 engine id
class Engine(PyVoiceEngine):
    def init(self):                         # 懒调；失败 raise → 侧栏 Failed
        self.catalog = scan_voicebanks()    # VoiceSourceInfos 契约：同步、永不阻塞，Init 期缓存
    def destroy(self): self.catalog = None
```

```python
# engine.py —— A 类成员逐一镜像（映射表 = scripting/wire/v1.json）
class PyVoiceEngine:
    def get_automation_configs(self, ctx):  # 声明面纯函数；ctx.parts[i].voice_id 分流多声库
        return { "Breathiness": tunelab.AutomationConfig(0, 1, display_text=tr("气声")) }
    def get_note_property_config(self, ctx):
        return tunelab.ObjectConfig(properties={
            "tension": tunelab.Slider.linear(0, -1, 1),
            "mode": tunelab.ComboBox(["normal", "whisper"]),
        })
    def get_phoneme_property_configs(self, ctx): return {}
    def create_session(self, context): return PySession(self, context)

class PySession:
    default_lyric = "a"
    def is_continuation(self, note): return note.lyric in ("-", "ー")

    def get_next_pending_synthesis_range(self, start, end):   # peek：只查本地增量表（§3.5 有宿主缓存兜底）
        return self.dirty.next_in(start, end)

    async def synthesize_next(self, start, end):
        piece = self.dirty.next_in(start, end)                 # commit：同窗口确定性重导出
        snap = await self.ctx.get_snapshot(piece.notes)        # 全量冻结快照（§5.5；求值器=句柄）
        self.dirty.clear(piece)
        out = await self.ctx.run_worker(self.render, snap)     # offload；发布自动 marshal 回控制线程
        # render 内：snap.pitch.evaluate(times)（曲线面）；phonemes = pinned or g2p(n.lyric)；
        # audio = acoustic(...)  → float32
        seg = self.ctx.create_audio_segment(int(piece.start*48000), len(out.audio), 48000)
        seg.write(0, out.audio)                                # numpy 视图直写 shm（§6.2）
        seg.commit()
        self.set_products(pitch=out.pitch, parameters={},
                          phonemes={n.id: syll for n, syll in zip(snap.notes, out.sylls)})
```

崩溃恢复、取消、进度、状态带均由 façade 与宿主包办（§4）；作者代码里没有任何跨进程概念——
协议藏在 `tunelab` 之内，这正是 façade 存在的意义。

### A.3 开发 → 打包 → 安装 → 使用

1. `pip install tunelab`；`pytest` 直接喂构造快照（façade 可脱离宿主单测）。
2. `python -m tunelab.serve --replay fixtures/part01.json`：假宿主回放变更流/窗口请求，本地断点调 render。
3. `python -m tunelab.build src/ dist/com.example.pyvoice.tlx` → 关 TuneLab → `install-tlx.ps1`
   （或应用内拖入/双击 .tlx 热装）。改代码 → 侧栏"重新加载"（kill+respawn+会话重建，免重启宿主）。
4. 探不到 runtime → Skipped + 【下载运行时】（§8.6）。
5. 使用 = 与 .NET 引擎不可分辨：侧栏条目/Introduction、设置桶、声库选择器、状态带、路由页（与其他
   `voice:PyVoice` 候选竞争同规则）、工程存 `engine` id。
