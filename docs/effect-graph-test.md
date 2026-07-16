# Effect 图（厚 processor / 每段一个）测试清单

本清单只覆盖「effect 链处理从宿主厚 / 插件薄改为厚 processor / 每段一个」这次重写**受影响的范围**，
不复测已通过的 voice 合成基线。重写要点（被测对象）：

- 每个「启用且引擎可用的 effect × 一个上游音频段」一个厚 `IEffectSynthesisProcessor`，自管该段失效与重处理；
- 失效自管：processor 订阅 `IEffectSynthesisContext`（`Input.Committed` / `Properties.Modified` / 自动化 `RangeModified`），
  于 `Committed` 触发 `ProcessingRequested`，宿主据此调度 `Process`；
- 段间独立、无共享上下文；链尾各输出段按绝对时间混音成最终音频（重叠相加）；
- bypass / 引擎缺失的 effect 整级 passthrough；
- 跨段/跨 part 并行，受设置 `MaxParallelSynthesisTasks` 全局封顶（≤0 按核数）。

## 前置

1. 已为你 build + pack 好测试插件：`tests/tlx/v1-effect.tlx`。在 App 内把该 `.tlx` 拖进窗口
   （或扩展侧边栏 Install Extension）安装并即时加载。
2. 任一 voice 源（能产出音频段即可），新建 MidiPart 画几个 note 让其合成出音频。
3. 添加效果器的菜单/效果器条上显示的是引擎 **Type id**（无独立显示名）：
   - **TLTestGain** —— 增益。参数面板有 `gain` 滑块（0~2，默认 1）+ `Show Gain Env` 勾选框；
     勾选时暴露连续自动化轨 `Gain Env`（gain_env）；恒有分段轨 `Formant`（formant，参照实现不消费）。
   - **TLTestReverse** —— 倒放。无参数、无自动化。
4. 日志在 `%APPDATA%/TuneLab/Logs/`，异常会落这里。

## 用例

### A. 单个 effect（基本通路）
1. 给 part 加 **TLTestGain**。拖 `gain` 滑块 → 合成音频幅度随之变（波形/试听）。
2. `gain=0` → 静音；`gain=2` → 放大。每次改完参数应能听到/看到更新（无需重新合成 voice）。

### B. per-effect 时间轴自动化（gain_env）
1. 确认 `Show Gain Env` 勾选，参数栏出现 `Gain Env` 轨。画一条曲线 → 对应时间段幅度按曲线起伏。
2. **下游跳过验证**（稀疏 part、多段音频时最明显）：只在**某一个音频段所在时间区间**改 gain_env，
   只有该段输出变，其余段输出不变（其余段不应重算波形）。
3. 取消 `Show Gain Env` 勾选 → `Gain Env` 轨消失，且增益回到只受 `gain` 标量控制（env 不再生效）。
   重新勾选 → 之前画的 gain_env 曲线**原样恢复**（孤儿数据保留）。

### C. 链串行与顺序
1. 加 **TLTestGain** 再加 **TLTestReverse**（Gain→Reverse）：先增益后倒放。
2. 调换顺序（拖动重排为 Reverse→Gain）→ 输出随顺序改变、且正确（链结构变 = 图重排）。

### D. bypass / 增删 / 重排
1. 禁用（bypass）链中某个 effect → 该级整体 passthrough（如禁用 Gain 则只剩 Reverse 效果）。
2. 重新启用 → 该级恢复处理。
3. 删除 / 新增 effect → 输出随之更新，无异常。

### E. 多段与重叠混音
1. 稀疏 part（音符间有空洞、产出多个独立音频段）：每段各自过链、互不影响。
2. 段间空洞处留白；若某段经 effect 产出尾巴与相邻段重叠，重叠区按时间相加混音（不应硬拼接/互相覆盖）。

### F. voice 重合成 / 采样率 / part 切换
1. 改 note 触发 voice 段重合成（重 Commit）→ 仅受影响段的 effect 重跑，其余段缓存复用。
2. 设置里改采样率 → 全部段按新率重做、effect 重处理，输出正确。
3. effect 处理进行中切换/关闭 part → 不崩、无残留（日志无异常）。

### G. 并行任务数设置
- 设置文件 `MaxParallelSynthesisTasks` 控制 voice 合成 + effect 处理的全局并行上限（≤0 = 按 CPU 核数）。
  本参照实现的 Gain/Reverse 计算极快、并行难肉眼观测，主要验证：设为 1 时仍正确串行完成；
  设为较大值时无竞态/无崩溃，输出与串行一致。

## 通过标准

- 上述各用例输出符合预期，编辑后及时刷新；
- 切换/删除/采样率变/进行中销毁均无异常（`%APPDATA%/TuneLab/Logs/` 无未捕获异常）；
- 下游跳过（用例 B.2）：与本段无关的自动化改动不触发该段及其下游重算。
