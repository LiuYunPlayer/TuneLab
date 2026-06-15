# 动态自动化轨声明（条件轨集合）测试用例

> 本文只覆盖**本次改动的受影响范围**：自动化轨集合由静态声明改为 **context 驱动的条件声明**——
> 轨集合 = f(当前参数值)，参数 commit 时宿主按当前值重算并刷新 UI（参数栏 + 属性侧栏默认值面板）。
> 配套孤儿数据策略：**轨从声明消失后已画曲线保留隐藏、轨复现即原样恢复**（数据层不裁剪）。
> voice 路径（材料化 + part 参数驱动）与 effect 路径（惰性缓存 + effect 参数驱动）分别验证。
> voice/effect 的基础功能（轨编辑/合成/链等）见各自基线测试文档，不在此重复。
> 每个用例：**做什么 → 预期看到什么**。

## 准备

```bash
dotnet build TuneLab.sln -c Debug
dotnet build tests/TestPlugins.slnx -c Debug
powershell -File tests/pack-tlx.ps1          # 打成 tests/tlx/*.tlx
```

- 安装：把 `tests/tlx/v1-effect.tlx` + `tests/tlx/v1-voice.tlx` 拖进 TuneLab 窗口（或扩展侧栏 → Install Extension）。
- 本次为测试新增的条件开关（UI 显示名）：
  - **声库 TestVoice**（v1-voice）：part 属性面板多了勾选框 **Enable Growl**（默认勾选）；勾选才暴露自动化轨 **Growl**。
  - **效果器 TLTestGain**（v1-effect）：参数面板多了勾选框 **Show Gain Env**（默认勾选）；勾选才暴露自动化轨 **Gain Env**。
- 两处"自动化轨"都同时出现在：**钢琴窗底部参数栏**的轨按钮 + **Properties 侧栏 → Automation** 默认值面板里那一行。两处都应随开关同步显隐。

---

## 一、voice 路径：part 参数驱动条件轨

### 1. 开关显隐轨（part 级）
- 选一条用 **TestVoice** 的 MIDI part，写几个 note 能正常合成。默认 **Enable Growl** 勾选 → 参数栏与 Automation 面板都能看到 **Growl** 轨。
- 取消勾选 **Enable Growl** → **Growl** 轨从参数栏与 Automation 面板**同时消失**。
- 重新勾选 → **Growl** 轨**重新出现**。

### 2. 孤儿数据保留（核心）
- 勾选状态下点亮 **Growl** 轨、画一条明显曲线（如前段 0、后段 80）。
- 取消勾选 **Enable Growl** → 轨消失、合成里 Growl 不再作用。
- 重新勾选 → **Growl 轨回来、曲线与取消前一模一样**（不丢、不被重置成默认基线）。

### 3. 合成正确性
- 画好 Growl 曲线、勾选时合成 → Growl 生效（按插件对 Growl 的处理体现在音频/产物上）。
- 取消勾选后合成 → Growl **不作用**（轨不在声明里，宿主跳过该轨）。
- 重新勾选合成 → 又按原曲线作用。

### 4. undo/redo 跟随
- 勾/取消 **Enable Growl** 可 undo/redo；轨的显隐随之回退/重做，曲线数据始终保留。

---

## 二、effect 路径：effect 参数驱动条件轨

### 5. 开关显隐轨（effect 级）
- 给 part 加效果器 **TLTestGain**。默认 **Show Gain Env** 勾选 → 参数栏与 Automation 面板能看到 **Gain Env** 轨。
- 取消勾选 **Show Gain Env** → **Gain Env** 轨从两处同时消失；重新勾选 → 重新出现。

### 6. 孤儿数据保留 + 音频跟随（核心）
- 勾选下点亮 **Gain Env**、画曲线（如前段 0、后段 2），合成 → 输出 = 输入 × gain × env(t)（前段静音、后段更响）。
- 取消勾选 **Show Gain Env** → 轨消失，**音频里 env 不再作用**（回到仅 × gain）。
- 重新勾选 → **Gain Env 轨与曲线原样恢复，音频重新按曲线增益**。

### 7. 多 effect 互不串扰
- 加两个 **TLTestGain**（链中两环）。只切其中一个的 **Show Gain Env** → 只有该 effect 的 **Gain Env** 行显隐，另一个不受影响（按 effect 索引分组、各自独立）。

---

## 三、去抖与不回归

### 8. 无关参数 commit 不抖动轨栏
- 拖动 **TLTestGain** 的 **gain** 滑块并松手（commit）→ 参数栏/Automation 面板的轨集合**不闪、不重建**（gain 不门控任何轨，签名未变 → 不触发刷新）。
- 编辑 part 上不门控轨的其他属性同理。

### 9. 静态声明插件不回归
- 用 **v1-suite** 或 **v1-i18n** 声库的 part：其自动化轨集合**恒定**，编辑任何参数都不应使轨消失/重复出现（静态插件忽略 context、返回固定集合）。

### 10. 换源 / 链增删仍正确
- 换声库 → 轨集合按新源重建（走既有换源刷新路径）。
- 加/删/重排 effect → 各 effect 的轨分组随链更新（走既有链变刷新路径）。
- 上述场景与本次改动前一致，无回归。

---

## 备注（验证侧重点）

- 用例 2/6 是本次设计的核心承诺——**孤儿数据保留隐藏、轨复现即恢复**：数据层从不因声明收缩而裁剪 `Automations` 曲线数据。
- 用例 8 验证**签名去抖**：只有轨集合实际变化（key/字段变）才发 `AutomationConfigsModified`，无关参数 commit 不应引发参数栏重建。这是静态插件不被新机制拖累的保证。
- voice 与 effect 走不同宿主路径（voice 材料化缓存 + part 参数驱动；effect 惰性 dirty 缓存 + effect 参数驱动），故 1~4 与 5~7 需分别验证。
