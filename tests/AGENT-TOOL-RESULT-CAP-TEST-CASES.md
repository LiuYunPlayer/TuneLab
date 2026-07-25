# Agent 工具结果中央上限测试用例

覆盖：单次工具结果回灌模型的中央硬上限（`AgentRunner.ClampToolResult` + `Settings.AgentMaxToolResultChars`
+ 设置窗 UI）。防某工具输出淹没上下文；宽默认、可调、结构性兜底。

## 前置

- Agent 侧栏已连模型；设置窗「常规」页可见「AI Agent 单次工具结果上限（字符）」滑条（zh-CN）。
- 准备一个**很长的脚本**存进库（源码 > 上限，用于 `read_script` 触发截断），或把上限临时调很低便于触发。

## 1. 默认宽、普通结果不受影响

1. 保持默认（40000）。让 agent 调 `list_extensions` / `list_sound_sources` / `get_project_overview` 等普通结果。
2. **期望**：结果**完整**、无截断标记（普通机器十几个音源/扩展远小于 4 万字符）。

## 2. 超上限即截断 + 指引

1. 把上限调低（如 2000），或用一个源码 > 上限的脚本 `read_script("<大脚本>")`。
2. **期望**：结果被截断为上限长度，尾部附 `[... tool result truncated: 2000 of N characters shown. If this is a list, narrow your query ... This limit is configurable in Settings.]`。
3. **展示与回灌一致**：对话里工具块显示的结果 = 模型收到的结果（都被截断，不会"显示全、回灌全"）。

## 3. 设置可调 + 持久化

1. 在设置窗把上限拖到别的值 → 关设置。
2. **期望**：新值即时生效（下次工具调用按新上限截断）；重启应用后仍是新值（存进 `settings.json` 的 `AgentMaxToolResultChars`）。
3. 设成很大（如 200000）→ 之前会截断的结果现在完整返回。
4. 设成 `0`（滑条下限=0，或直接在数值标签键入 0）→ 不限（不截断）。数值标签可**键入精确值**（如 2000）便于测试。

## 4. i18n

1. zh-CN 下设置标签显示「AI Agent 单次工具结果上限（字符）」；其它语言回退英文源（与仓库现状一致，不为此单条补 14 语言）。

## 回归检查（不应被破坏）

- 各工具自带的友好上限仍在：`get_extension_readme` 20000、`list_sound_sources` 音源 300、脚本 `print/log` 16KB——这些在中央上限**之下**先生效（给更贴心的提示）。
- 正常（未超限）工具结果**逐字节不变**（clamp 只在超限时动手）。
- 错误结果（短）不受影响。
- 中央 clamp 在唯一入口，覆盖全部 13 工具及未来新工具（含将来的沙箱合成结果）。
