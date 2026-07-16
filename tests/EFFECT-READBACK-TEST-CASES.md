# 效果器回显轨 + 回显富类型换形 测试用例

> 本文覆盖**本次改动的受影响范围**，两件事：
> 1. **回显富类型换形**：`SynthesizedParameters` 的曲线数据从裸 `IReadOnlyList<IReadOnlyList<Point>>` 换成
>    具名富类型 `SynthesizedParameter`（含 `Segments`）。voice 回显行为**不变**——本文只验证它没回归，细节见
>    `SYNTHESIZED-PARAMETER-READBACK-TEST-CASES.md`，不重复。
> 2. **效果器回显轨（新）**：effect 引擎也能像 voice 一样暴露**一等只读回显轨**。轨形态由引擎经
>    `IEffectSynthesisEngine.GetSynthesizedParameterConfigs(context)` 声明（独立 key、自带 DisplayText/Min/Max/Color、
>    分段形 `DefaultValue=NaN`），曲线数据经 `IEffectSynthesisProcessor.SynthesizedParameters` 按同一批 key 承载、由宿主
>    聚合该 effect 各段后呈现。宿主把 voice 与各 effect 的回显轨**按源统一**到参数区标题栏，复用同一套显隐/绘制。
>
> 回显是**只读呈现层**：不可编辑、不可激活、不进数据层、不参与序列化（与 voice 回显同构）。
> 合成正确性、链调度、bypass、增删/重排等见各自基线测试文档，不在此重复。
> 每个用例：**做什么 → 预期看到什么**。

## 准备

```bash
dotnet build TuneLab.sln -c Debug
dotnet build tests/TestPlugins.slnx -c Debug
powershell -File tests/pack-tlx.ps1          # 打成 tests/tlx/*.tlx
```

- 安装：把 `tests/tlx/v1-voice.tlx` 与 `tests/tlx/v1-effect.tlx` 拖进 TuneLab 窗口（或扩展侧栏 → Install Extension）。
- 涉及的 UI 显示名：
  - **声库**：`Alice (V1 Test)` / `Bob (V1 Test)`（v1-voice），其回显轨 **Energy**（粉 `#E573B0`，0..100）。
  - **效果器**（链上添加，按 **Type id** 显示）：`TLTestGain`、`TLTestReverse`。
  - **effect 回显轨**：`TLTestGain` 产 **Loudness**（蓝 `#00B0FF`，0..2），由引擎**恒声明**；`TLTestReverse` **无回显**。
  - 显隐按钮都在**参数区标题栏右侧**（小眼睛 + 文本），voice 与各 effect **按源分组**：每组前有一个淡色**源标签**
    （`Voice` / 各 effect 的 Type 名），组间留更大间距。
- 参照实现的回显数据：`TLTestGain` 每处理一段就为该段产一条 **Loudness** 包络回显（对输出按 ~20ms 窗算 RMS），
  **只在该 effect 处理完成后出现**，触发合成即播放经过该 part / 走调度。

---

## 一、回显富类型换形不回归（voice 侧）

### 1. voice 回显照常
- 按 `SYNTHESIZED-PARAMETER-READBACK-TEST-CASES.md` 跑一遍 Energy 回显：标题栏 **Voice** 组下有 **Energy** 按钮、
  点亮后合成出现粉色积分面积、段间断开、缩放跟随——**与换形前完全一致**（富类型只换了数据载体，呈现不变）。

---

## 二、效果器回显声明与显隐（标题栏分组）

### 2. 加 Gain 即出现 Loudness 按钮
- 选一条 part（任意声库），底部/侧栏给它的 effect 链**添加 `TLTestGain`**。
- 看参数区标题栏右侧 → 出现一个新分组：淡色源标签 **TLTestGain** + 一个 **Loudness** 按钮（小眼睛 + 文本，蓝）。
  若该声库也有 voice 回显（如 Alice 的 Energy），则两组并列：`Voice 👁Energy` ⋯⋯ `TLTestGain 👁Loudness`，组间留更大间距。
- 默认**未点亮**（眼睛灰白），参数区此时不画 effect 回显。

### 3. 点亮 / 熄灭 Loudness
- 单击 **Loudness** → 点亮（眼睛变蓝）。触发合成（播放经过该 part）→ 每段 Gain 处理完成后，参数区出现
  **蓝色半透明积分面积**（Loudness 包络），即 effect 回显。熄灭 → 立即消失（数据仍在引擎侧，重新点亮即恢复，无需重合成）。

### 4. 改 gain 实时反映在回显
- 点亮 Loudness 并合成出曲线。把 `TLTestGain` 的 **gain** 滑块调大 → 重新处理后 Loudness 面积**整体抬高**
  （输出更响、RMS 更大）；调小 → 压低。验证回显确由处理产出、随参数走。

### 5. Reverse 无回显
- 给链再加 `TLTestReverse`（或单独加）。标题栏**不出现** `TLTestReverse` 分组、无 Loudness 之外的按钮
  （Reverse 的 `GetSynthesizedParameterConfigs` 为空）。

---

## 三、多源分组与路由

### 6. 多个 effect 各自成组
- 链上放两个 `TLTestGain`（Gain → Reverse → Gain 之类）。标题栏出现**两个** `TLTestGain` 分组，各带自己的
  **Loudness** 按钮，互不串色/串显隐：点亮第一个不影响第二个。
- 分别点亮 → 两条蓝色面积分别对应**各自那一级**处理后的响度（后级在前级之后，量级可不同）。

### 7. 增删 / 重排即时刷新
- 删除一个 `TLTestGain` → 其分组**立即从标题栏消失**（不必把鼠标移上标题栏）。重排链顺序 → 分组顺序随之更新。

### 8. 标题栏手势共存
- 在标题栏**空白处**按住上下拖 → 参数区高度改变（沿用拖拽改高）。
- 在任一回显按钮（Voice 的或 effect 的）上按下 → 只切换该轨显隐，**不触发拖拽改高**。

---

## 四、绘制 / 不回归

### 9. 多源同屏叠加
- 同时点亮 Alice 的 **Energy**（粉）与 `TLTestGain` 的 **Loudness**（蓝），并画一条可编辑轨（如 voice 的 Growl 橙线）。
- 合成 → 粉色 Energy 面积 + 蓝色 Loudness 面积 + 橙色 Growl 细线**同屏共存**，各按自己 config 色、互不干扰。

### 10. 缩放 / 横向滚动跟随
- 横向缩放、左右滚动 → effect 回显面积**随时间轴正确缩放/平移**，与音频段、voice 回显、可编辑曲线对齐，不漂移。

### 11. 只读、不可激活
- effect 回显轨**不在底部 tabbar**、不能设为激活编辑轨；对着蓝色面积做绘制/擦除/拖锚点 → **无任何效果**。

### 12. 无 effect / 旧工程不回归
- part 不挂任何 effect：标题栏与改动前一致（只有 voice 回显分组，或无回显则无按钮）。
- 既有可编辑 effect 自动化轨（如 `TLTestGain` 的 **Gain Env**、分段 **Formant**）：参数栏绘制**与改动前一致**
  （细线、可编辑、可激活、进底部 tabbar），不因回显改动出现多余填充或异常。

---

## 备注（验证侧重点）

- effect 回显与 voice 回显**同构**：只读、按 `AutomationKey` 分源（voice / effect+index）统一到同一套标题栏与绘制。
- 回显数据**按 effect 聚合该 effect 的各段**（一段一 processor），段按起始时间升序拼接；段间断开、不跨段连线。
- 时间系是**秒**（与音频产物一致），宿主按 tempo 换算到屏幕 x；用例 10/11 验证换算与只读。
- effect 回显**不进工程序列化**（重开工程后未重合成则无数据，属预期；但 chip 因引擎恒声明仍在）。
- 视觉：积分面积 = 曲线与参数区底部围成的填充区，用轨 config 色、低透明度（约 0.25）、仅填充无描边——宿主侧渲染选择，可后续微调。
