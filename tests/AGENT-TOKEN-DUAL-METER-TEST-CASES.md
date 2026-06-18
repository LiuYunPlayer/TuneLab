# Agent token 双口径显示修复测试用例

覆盖范围：消解"助手消息脚注的 token 数"与"输入框上方状态行 Context 数"在多工具往返时看着对不上的疑惑。脚注 = 本轮**各次模型调用 total 之和**（工具往返重复前缀）；状态行 Context = **末次**调用的输入+输出（≈当前上下文）。本次只改脚注的显示口径（多次调用标注 "· N calls" + tooltip 桥接到 Context），不改任何计数逻辑。仅测本范围，token 计数/状态行基线另见 agent 进度。

## 测试前置

1. 构建并以新构建启动（确保无其它 TuneLab 实例占单实例锁）。
2. 配好一个会返回 usage 的 OpenAI 兼容模型（端点须回 usage；不回则脚注无 token 数、本用例不适用）。

## 用例

| # | 操作 | 预期 |
|---|---|---|
| T1 | 发一条**不触发工具**的普通问题 | 助手脚注显示 `X tokens`（无 "· N calls"）；hover=`Input … · Output …`。状态行 `Context ≈X · Session …`，与脚注同量级、不矛盾 |
| T2 | 发一条**触发多次工具往返**的任务（如"新空轨写 8 小节旋律"，会 add_part + 多次 apply_edits） | 脚注显示 `X tokens · N calls`（N=本轮模型调用次数，>1）；hover 三行：`Total of N model calls this turn …` / `Input … · Output …` / `Last call context ~Z tokens` |
| T3 | 核对 T2 的 tooltip 末行 `Last call context ~Z` 与状态行 `Context Z` | 两者数值一致（同口径=末次调用 prompt+completion）。脚注大数（合计）与 Context（末轮）差异由此被解释，不再"对不上" |
| T4 | 重载该会话（☰ 切走再切回 / 重开应用） | 重载后的脚注与实时一致：单轮无 "· N calls"，多轮有 "· N calls" 且 tooltip 同样三行、Last call context 与状态行 Context 吻合（重载==实时） |
| T5 | 轮边界**插话**后继续（生成中 Enter 插话被采纳、续跑） | 插话使本轮模型调用次数增加，脚注 N calls 随之增大；末次上下文仍与状态行 Context 一致 |

## 生成过程中的运行 token（左下角脚注）

| # | 操作 | 预期 |
|---|---|---|
| T6 | 发一条触发多次工具往返的任务，观察生成过程 | 生成中**左下角脚注位置实时显示运行 token 合计**（`X tokens`，hover=`Input … · Output …`），随每次工具往返调用完成而累加；原来的三点动画移到其**右侧**。tool 块上方/下方**不再**有 per-call 小行 |
| T7 | 生成完成后 | 左下角运行脚注随底部一行整体移除，最终脚注 `合计 · N calls` 出现在内容区左下角（与原行为一致）；运行过程中的累加值 ≤ 最终合计（运行值含各工具往返、末轮收尾计入最终脚注） |
| T8 | 重载该会话 | 重载是静态完成态：只显示最终脚注 `合计 · N calls`，无运行行/三点动画（重载==实时的最终态）|

## 回归

- R-1：端点**不返回 usage** 时，脚注只剩 Copy、状态行隐藏（与之前一致，不报错）。
- R-2：Copy 按钮仍复制本条助手原文。
