# Effect 功能测试用例

> 本文只覆盖**效果器（effect）新功能**的受影响范围；format/voice 等既有功能用例见 [PLUGIN-TEST-CASES.md](PLUGIN-TEST-CASES.md)，不在此重复。
> 每个用例：**做什么 → 预期看到什么**。

## 准备

```bash
dotnet build TuneLab.sln -c Debug
dotnet build tests/TestPlugins.slnx -c Debug
powershell -File tests/pack-tlx.ps1          # 打成 tests/tlx/*.tlx
```

- 安装：把 `tests/tlx/v1-effect.tlx` 拖进 TuneLab 窗口（或扩展侧边栏 → Install Extension）。
- 测试引擎：`v1-effect` 包含两个 effect —— **TLTestGain**（参数 `gain`，0~2，默认 1，输出 = 输入 × gain）与 **TLTestReverse**（无参数，倒放样本）。
- 操作入口：选中一条 **MIDI part**，右侧 **Properties** 侧栏的 **Effects** 面板。先写几个 note 并能正常合成出声，作为加 effect 前的基线。

---

## 一、加载与发现

### 1. 安装加载
- 装 `v1-effect.tlx` → 扩展侧边栏 **Loaded**，类别显示 **effect**（不再是「effect extensions are not supported」）。
- 卡片信息正常（名称 / 作者 / 版本 / 简介 tooltip）。

### 2. 引擎出现在 Add 菜单
- 选中一条 MIDI part → Effects 面板点 **「+ Add Effect」** → 菜单同时列出 **TLTestGain** 与 **TLTestReverse**（一包两引擎都注册）。

---

## 二、单个 effect

### 3. 加 Gain，参数面板可见
- Add → TLTestGain → 链中出现一行「TLTestGain」，下方参数面板出现 **gain 滑块**（默认 1）。
- gain=1 时播放 → 与未加 effect **听感一致**（×1 不改变）。

### 4. 参数生效 + 重算
- 把 gain 拖到 **0** → 该 part **静音**（×0）。
- 拖到 **2** → 明显**更响**（×2，注意不要削顶失真即可）。
- 改 gain 后无需任何额外操作，音频**自动重新渲染**（波形/响度随之变化）。

### 5. bypass（启用开关）
- 取消勾选该 effect 行的**启用复选框** → 该 effect 被旁路，音频回到**加 effect 前**（passthrough）。
- 重新勾选 → 效果恢复。

### 6. 删除
- 点该 effect 行的 **✕** → effect 移除，音频回到原始；面板该行消失。

---

## 三、效果链

### 7. 多个 effect 串联
- 依次 Add **TLTestGain**（gain=0.5）和 **TLTestReverse** → 链显示两行，**按顺序串行**：先 ×0.5 再倒放。
- 播放 → 音频既变小声、又倒放。

### 8. 重排
- 用 effect 行的 **↑ / ↓** 调整顺序 → 链顺序随之改变，音频按新顺序重渲染（Gain→Reverse 与 Reverse→Gain 对幅度一致、但与各自 bypass 组合可区分；主要验证重排不报错、能重算）。

### 9. 一条 part 加同一引擎两次
- Add 两个 **TLTestGain**（各 0.5） → 两级串联 = ×0.25（更小声），验证同类型可多实例、各自独立参数。

---

## 四、健壮性

### 10. 引擎缺失 → passthrough
- 在装有 effect 的工程里加几个 effect 并保存；**卸载 v1-effect** 后重开该工程 → part **仍能播放**（缺失引擎按直通处理），**不崩溃**，日志/状态有提示。

### 11. 持久化 + 撤销
- 加 effect、调参数后**保存工程**并重开 → effect 链、顺序、启用状态、参数**完整恢复**。
- 加/删/改 effect 后 **Ctrl+Z 撤销 / Ctrl+Y 重做** → 链与参数正确回退/前进，音频随之重算。

### 12. 既有工程不受影响（无回归）
- 打开**不含任何 effect** 的旧工程 → 合成/播放行为与加本功能前**完全一致**（无 effect 时直接取 voice 输出）。

---

> **本版范围说明**：effect 的**参数**通过 Properties 面板编辑；**per-effect 时间轴自动化曲线**的编辑 UI 本版未做（数据模型与 SDK 已支持，曲线编辑后补）。effect 面向**离线整段音频变换**（如 SVC），按合成片段独立处理，不是实时 VST。
