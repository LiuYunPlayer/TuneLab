# voice 声明上移到引擎 + 构造期订阅 测试用例

> 本文只覆盖**本次改动的受影响范围**：voice 的声明面（`GetAutomationConfigs` / `GetSynthesizedParameterConfigs` /
> `GetPartPropertyConfig` / `GetNotePropertyConfig`）从 `IVoiceSession` 上移到 **`IVoiceEngine`**，`voiceId` 经
> **`IVoicePartPropertyContext.VoiceId`** 注入；会话只余 `DefaultLyric`。宿主据此在**建会话之前**填好声明，使会话
> **构造期即可订阅自己声明的自动化轨**（修复"绘制完参数不触发重渲"的根因）。
>
> 轨编辑/合成/孤儿数据/条件显隐的既有行为应**完全不变**（仅声明来源从 session 改为 engine）——本文验证"行为不回归"
> + "构造期订阅生效"两条主线。基础功能见 [VOICE-SESSION-TEST-CASES.md](VOICE-SESSION-TEST-CASES.md) /
> [DYNAMIC-AUTOMATION-DECLARATIONS-TEST-CASES.md](DYNAMIC-AUTOMATION-DECLARATIONS-TEST-CASES.md) /
> [PROPERTY-CONDITIONAL-TEST-CASES.md](PROPERTY-CONDITIONAL-TEST-CASES.md)，不在此重复。
> 每个用例：**做什么 → 预期看到什么**。

## 准备

```bash
dotnet build TuneLab.sln -c Debug
dotnet build tests/TestPlugins.slnx -c Debug
powershell -File tests/pack-tlx.ps1          # 打成 tests/tlx/*.tlx
```

- 安装：把 `tests/tlx/v1-voice.tlx` + `tests/tlx/v1-suite.tlx` 拖进 TuneLab 窗口（或扩展侧栏 → Install Extension）。
- 涉及声库（UI 显示名）：
  - **Alice (V1 Test)** / **Bob (V1 Test)**（v1-voice，引擎 `TLTestVoice`）：part 面板有 **Enable Growl**（默认勾选）；勾选才暴露自动化轨 **Growl**；另有恒在的分段轨 **Bend** 与只读回显轨 **Energy**。
  - **Suite … Voice** / **Suite … Conditional**（v1-suite，引擎 `TLSuiteVoice`，**同一引擎、两个 voiceId**）：Voice 库有自动化轨 **Power** + 多层嵌套 note 面板；Conditional 库无可编辑轨、note 面板随当前值动态变化。

---

## 一、核心：构造期订阅自定义轨 → 绘制触发重渲（原 bug）

### 1. 画自定义轨立即重渲
- 选一条用 **Alice (V1 Test)** 的 part，写几个 note 正常合成（状态带变绿 Synthesized）。
- 点亮 **Growl** 轨、画一条曲线。
- **预期**：相交块立刻转 Pending → Synthesizing → Synthesized，重新合成（不是毫无反应）。这是修复点——构造期已订上 Growl 的 `RangeModified`。

### 2. 不需要"先动别的"来唤醒
- 新建/换到一条 Alice part 后，**第一件事就是画 Growl**（中间不碰 note、不改 part 属性）。
- **预期**：照样触发重渲。验证声明在建会话前已填好、构造期订阅一次到位（旧实现里构造期 `TryGetAutomation` 必失败、要靠别的变更才间接重渲，甚至完全不重渲）。

### 3. 换声源后仍生效
- 把该 part 从 Alice 换成 **Bob (V1 Test)**（会重建会话）→ 画 Growl。
- **预期**：重渲正常。换源后新会话构造期重新订上 Growl。

### 4. Pitch 一直正常（对照）
- 画 Pitch 曲线 → 重渲。这条在改动前后都正常（Pitch 不走自定义轨代理），作为"非回归"对照。

---

## 二、voiceId 分流（同一引擎多声库，声明按 context.VoiceId 区分）

### 5. 两库声明不同
- 一条 part 用 **Suite … Voice**：参数栏有 **Power** 轨；note 属性面板是多层嵌套（tension/accent/label/style/quality/vibrato→lfo→range）。
- 另一条 part 用 **Suite … Conditional**：**无 Power 轨**；note 面板是 mode/letters + 动态字段。
- **预期**：两库各自正确——证明引擎按 `context.VoiceId` 返回了不同声明（同一个 `SuiteVoiceEngine` 实例服务两个 voiceId）。

### 6. Conditional 动态面板仍随值变（回归）
- Conditional part：选中 note，改 **letters**（如输入 "abc"）→ 面板按字符派生滑条；mode 切 **Advanced** → 多出 gain/detail；勾 part 的 **fromPart** → note 多出 partGain。
- **预期**：与 [PROPERTY-CONDITIONAL-TEST-CASES.md](PROPERTY-CONDITIONAL-TEST-CASES.md) 一致（声明搬到引擎后动态面板行为不变）。

---

## 三、回归：条件轨显隐 + 孤儿数据（声明来源变了、行为不变）

### 7. Enable Growl 显隐 + 孤儿数据
- Alice part：画好 Growl 曲线 → 取消勾 **Enable Growl** → Growl 轨从参数栏与 Automation 面板同时消失、合成里不作用。
- 重新勾选 → **Growl 轨回来、曲线与取消前一模一样**。
- **预期**：与 [DYNAMIC-AUTOMATION-DECLARATIONS-TEST-CASES.md](DYNAMIC-AUTOMATION-DECLARATIONS-TEST-CASES.md) 用例 1–4 完全一致。

### 8. 分段轨 / 回显轨仍在
- Alice part：**Bend** 轨恒可见可画（分段形）；合成后 **Energy** 回显轨在 Automation 标题栏可显隐、绘制只读曲线。
- **预期**：与基线一致。

### 9. 属性面板默认值面板
- Properties 侧栏 → part/note 面板正常渲染（数据来自引擎 `GetPartPropertyConfig`/`GetNotePropertyConfig`，经 `context.VoiceId` 求值）。改值、多选三态、嵌套导航均如常。

---

## 四、空声源 / 无声源回退

### 10. 无声源 part 不报错
- 一条**未指定声源**（Empty Voice）的 part：参数栏只有内置通用轨、无自定义轨；属性面板空；不崩、不重渲风暴。
- **预期**：空引擎声明全空（声明走引擎层的空引擎回退），行为同改动前。

---

## 自动化校验（可选，开发期）

- 主工程 + 插件 + legacy 全绿：
  ```bash
  dotnet build TuneLab.sln -c Debug
  dotnet build tests/TestPlugins.slnx -c Debug
  dotnet build legacy/compat/TuneLab.Hosting.Compat.Legacy/TuneLab.Hosting.Compat.Legacy.csproj -c Debug
  ```
- legacy 声源（经 compat 适配器）：用例 1/7/8 的等价行为应同样成立——legacy 引擎适配器按 voiceId 懒建"声明用"声源、缓存其转换后的 config，会话构造期同样订上各声明轨。
