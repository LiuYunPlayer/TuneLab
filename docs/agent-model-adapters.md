# Agent 模型适配器（宿主内部模块 · 贡献指南）

> 面向想给 TuneLab 接入新 LLM 提供方（模型协议）的贡献者。**agent-model 不是插件类型**——适配器随宿主源码走 PR 合入，不经 `.tlx` 扩展分发。

## 为什么不开放为插件（2.0 决策）

- **迭代速度错配**：模型协议面追赶外部世界的快速演进（多模态、流式工具调用、reasoning、缓存计量……），冻进插件 ABI 会让宿主最活跃的功能区背上最重的兼容枷锁。收在宿主内，接口随需求自由重构、无兼容负担。
- **生态形态**：适配器天然少（协议高度收敛：OpenAI-compatible 事实标准 + 少数几家）、小（纯 HTTP + JSON 翻译）、无私有资产——PR 门槛低、维护集中，插件机制的收益（分发私有实现）在此不成立。
- 将来若开放为插件点（把合同类型加回 SDK + 打开外部扫描，纯加性、零破坏），先决整改 checklist 见 issue #147 item 27 与 `IAgentModelEngine` 头注释（SendAsync 重载阶梯收敛为 callbacks 聚合、`AgentTokenUsage` int→long、枚举未知值容忍契约、DIM 政策头等）。

## 合同面在哪

`TuneLab/Agent/Contracts/`（命名空间 `TuneLab.Agent`）：

| 文件 | 内容 |
|---|---|
| `IAgentModelEngine.cs` | 引擎：`GetPropertyConfig`（设置面板声明）/ `Init` / `Destroy` / `CreateSession(PropertyObject)` |
| `IAgentModelSession.cs` | 会话：`SupportedInput`（模态声明，DIM 默认 Text）+ `SendAsync` 三级重载（非流式 / 正文流 / 正文+推理流，逐级 DIM 回退） |
| `IAgentModelPropertyContext.cs` | 设置面板 config 求值上下文（已填稀疏值，条件显隐字段用） |
| `AgentChat.cs` | 对话协议 DTO：`AgentMessage`/`AgentToolCall`/`AgentToolSchema`/`AgentModelRequest`/`AgentModelReply`/`AgentTokenUsage` 等 |

参照实现：`TuneLab/Agent/Models/OpenAICompatibleEngine.cs` + `OpenAICompatibleSession.cs`（内置 OpenAI 兼容端点适配器——多数新提供方兼容此协议，先确认是否真需要新适配器）。

## 添加一个新适配器（PR 步骤）

1. **实现两接口**：在 `TuneLab/Agent/Models/` 新建 `XxxEngine.cs` / `XxxSession.cs`。
   - `GetPropertyConfig`：声明用户可填的配置（端点、密钥、模型名等）。密钥字段用 `TextBoxConfig` 的 `IsPassword = true`；须为纯函数（同输入同输出、轻量），可按已填值条件显隐字段。
   - `CreateSession(properties)`：用用户确定后的配置值建会话，配置语义由你自己解释（与你声明的 config 对应）。
   - `SendAsync`：把 `AgentModelRequest`（对话历史 + 工具声明）翻译成该家协议发出，把响应装回 `AgentModelReply`（文本 / 工具调用 / usage / reasoning）。**只需实现非流式基础重载即可工作**（两级流式重载有 DIM 回退）；支持流式再覆盖对应重载，把增量经 `IProgress<string>` 回调。
   - 失败抛异常（宿主在调用边界 catch 并展示）；尊重 `CancellationToken`。
   - 支持图片输入则覆盖 `SupportedInput => AgentModality.Text | AgentModality.Image`，并消费 `AgentMessage.Parts`（多模态分片；不支持则只读 `Content` 纯文本拍平值即可）。
2. **注册**：在 `AgentModelManager.LoadBuiltIn`（`TuneLab/Extensions/Agent/AgentModelManager.cs`）加一行：
   ```csharp
   RegisterEngine(ExtensionManager.BuiltInPackageId, "your-engine-id", "Your Display Name", new TuneLab.Agent.Models.XxxEngine());
   ```
   `engine id` 是不可变身份（写进用户设置引用），起名后不要再改；显示名可改可译。
3. **协议 DTO 语义速记**：
   - `AgentMessage`：`Parts` 有值时以它为真源、`Content` 是文本拍平便利值；`ToolCalls` 仅 Assistant、`ToolCallId` 仅 Tool（回指 `AgentToolCall.Id`）。
   - `AgentToolCall.ArgumentsJson` / `AgentToolSchema.ParametersJsonSchema`：原始 JSON 文本，宿主与协议中立，由你翻译成该家的 tools/functions 字段。
   - `AgentModelReply.Reasoning`：推理模型「思考」全文，仅供展示/持久化、不回发给模型；`Usage` 端点未返回时给 null。
4. **验证**：build 后启动应用，在 Agent 侧栏的模型设置里应能看到你的引擎、填好配置后可对话；工具调用走一轮（宿主的 run_script 等工具会喂给 `AgentModelRequest.Tools`）。

## 线程与生命周期

- 引擎 `Init` 惰性（首次被使用时调）、失败抛异常即优雅降级。
- 会话 `IDisposable`：宿主切换模型/关闭会话时 Dispose，释放 HTTP 资源。
- `SendAsync` 由宿主 await；增量回调（`IProgress<string>`）在你的任意线程同步调用即可——宿主侧的事件管线（`AgentRunner` 的 SyncProgress → UI 层 `Progress<AgentEvent>`）负责按序送回 UI，适配器不必关心线程。
