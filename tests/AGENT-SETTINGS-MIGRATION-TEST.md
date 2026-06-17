# Agent 设置迁移到通用扩展设置系统 测试用例

验证 agent 模型设置从自定义 `AgentSettingsStore`/`AgentSettings.json` **退役**、改用通用 `ExtensionSettingsStore`：
- 各 provider 的配置存进 `ExtensionSettings.json` 的 `agent-model:<engineId>` 桶（原生 JSON、密钥加密）；
- 选中的 provider 存进 app `Settings.json` 的 `AgentModelProvider`；
- agent 侧边栏 UI（provider 选择 + 属性面板 + Submit + 聊天）行为不变。
只测受影响范围（设置持久化），不涉及聊天/工具链本身。

## 前置

- 需要一组可用的 OpenAI 兼容端点 + API Key（与平时用 agent 一样——Submit 成功才会落盘）。
- 内建 provider 只有 **openai-compatible**（UI 显示名见 combo）。
- 相关路径（Windows）：
  - agent 设置：`%AppData%/TuneLab/Configs/ExtensionSettings.json` → 键 `agent-model:openai-compatible`
  - 选中 provider：`%AppData%/TuneLab/Configs/Settings.json` → `AgentModelProvider`
  - 旧文件：`%AppData%/TuneLab/Configs/AgentSettings.json`（已退役，不再读写）

## 用例

### 1. 填写 → Submit → 落盘
- 开 App → agent 侧边栏 → ⚙ 设置 → 选 provider、填端点/API Key/模型名 → Submit（连接成功 → 回到聊天、顶部提示已连接）。
- **预期**：
  - `ExtensionSettings.json` 出现键 `agent-model:openai-compatible`，其下是**原生 JSON** 字段（端点/模型名明文字符串、**api_key 为 DPAPI 密文字符串**，非明文）。
  - `Settings.json` 的 `AgentModelProvider` = `openai-compatible`。
  - **不再生成/写入 `AgentSettings.json`**（若之前存在是旧的，时间戳不应更新）。

### 2. 重启自动接入 + 回显
- 完全退出重开 App。
- **预期**：agent 启动即自动连接（顶部"Connected to …"提示），无需再 Submit；进设置页，端点/模型名原样、API Key 掩码（已填非空）。

### 3. 旧文件退役不影响
- （可选）手动删除旧的 `AgentSettings.json` → 重开 App → agent 仍正常自动接入（说明已完全不依赖旧文件）。

### 4. 密钥安全（与扩展设置一致）
- `ExtensionSettings.json` 里 `agent-model:openai-compatible` 的 `api_key` 值是 base64 密文（Windows DPAPI）/ macOS 为 `""`（真密钥在钥匙串）。明文不落盘。

## 注意

- Submit **连接失败不落盘**（与原行为一致：先连通才保存）。要测持久化需一组能连通的凭据。
- 多 provider「各记一份设置」目前内建只有一个 provider，无法直接对比；待有第二个 agent-model 插件时再验证切换记忆。
- agent 设置**不**出现在「设置」窗口的「扩展」分页（它在 agent 侧边栏；扩展页只列实现 IExtensionSettings 的 voice/effect 等）。
