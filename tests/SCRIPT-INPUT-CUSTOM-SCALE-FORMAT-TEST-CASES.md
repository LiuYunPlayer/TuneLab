# 脚本入参自定义 scale/format 回调测试用例

覆盖入参 config 门面第 3 层：`NormalizedScale.custom(toValue, toNormalized)` 与
`NumberFormat.custom(format, parse)`——两个 JS 闭包包成 `INormalizedScale`/`INumberFormat` 适配器，
在入参窗存续期、UI 线程、逐拖拽/编辑时被调。只测本切片；内置 linear/integer/decimals 与响应式重算在别处。

## 前置

- 开一个含 midi part、选中若干音符的工程。
- 库内存一个带自定义回调的工具脚本 **`gain-tool`**（经 save_script 或手放 `%APPDATA%/TuneLab/Scripts`）：
  ```js
  function getScriptInfo() { return { name: 'Custom Gain', context: 'note', id: 'gain-tool' }; }
  function getInputConfig(ctx) {
    // 对数轴音量 0.01..10（线性拖动=指数变化）+ dB 显示/解析
    const logGain = NormalizedScale.custom(
      p => 0.01 * Math.pow(10 / 0.01, p),
      v => Math.log(v / 0.01) / Math.log(10 / 0.01));
    const dB = NumberFormat.custom(
      v => (20 * Math.log10(v)).toFixed(1) + ' dB',
      s => { const m = parseFloat(s); return isNaN(m) ? null : Math.pow(10, m / 20); });  // dB 的逆
    return { gain: SliderConfig.create(1, logGain).withFormat(dB) };
  }
  function main(inputs) { print('gain=' + (inputs.gain ?? 1)); }
  ```

## 1. 自定义标度（对数轴）

1. 经**音符右键菜单**运行 `Custom Gain` → 弹入参窗。
2. **期望**：滑柄初始停在 value=1 对应的归一化位置（≈ 中偏左，因 0.01..10 对数轴上 1 的归一化 ≈ 0.66）；
   把滑柄拖到正中（p=0.5）→ value ≈ 0.01·(1000)^0.5 = 0.316（非线性中点，验证 `toValue` 生效）。
3. 点 OK → Script 侧栏/日志 `gain=<拖到的实际 value>`（是 value 非归一化位置；main 读到的是标度解出的真实值）。

## 2. 自定义格式（单位 dB，显示 + 解析往返）

1. 再开入参窗。**期望**：数值框显示形如 `0.0 dB`（value=1 → 20·log10(1)=0 dB），非裸 `1`。
2. 在数值框键入 `6 dB`（或 `6`）回车 → **期望** `parse` 把 6dB 解回对应 value、滑柄随之移动、显示回 `6.0 dB` 往返一致。
3. 键入乱码 `abc` 回车 → **期望** `parse` 返 null=解析失败：值不变（拒绝非法输入），不崩、不写入 NaN。

## 3. 长开窗约束不累积（回调自重置）

1. 开入参窗后**放置 > 60 秒**（超过 Interactive 时限），再反复拖动滑柄几十次。
2. **期望**：全程流畅，无 `TimeoutException`/`statements exceeded`——`engine.Invoke` 每次自重置约束，回调不跨调用累积。

## 4. 回调抛错/返非法值不崩 UI

1. 临时把脚本 `toValue` 改成 `p => { throw new Error('boom'); }`（或返回字符串），重载脚本。
2. 运行 → 拖动滑柄。**期望**：滑柄降级（值取 NaN/回退），**应用不崩溃**；format 同理（返非串→回退不变式字面量）。修回正确函数即恢复。

## 5. get_script_inputs 读自定义 schema（线程亲和）

1. 让 agent 调 `get_script_inputs("gain-tool")`。
2. **期望**：正常返回，不崩、不报线程错——schema 求值 + 文本化（会调 `Scale.ToValue(0/1)`、`Format`）都在同一次 UI 线程 InvokeAsync 内完成。
   `gain` 行范围显 `[0.01, 10]`（ToValue(0)/ToValue(1)）、default 形如 `0.0 dB`（自定义 format 格式化默认值）。

## 6. 引擎生命周期（关窗即释、可重开）

1. 开入参窗 → 拖动几次（引擎经 config 引用链保活）→ 取消/OK 关窗。
2. 反复开关多次。**期望**：每次都正常渲染与回调；无残留状态串味、无内存暴涨（旧引擎关窗失引后 GC 回收）。

## 回归检查（不应被破坏）

- 内置 `SliderConfig.integer/linear`、`NumberFormat.decimals` 的脚本入参窗行为不变（无自定义回调=configs 不持引擎，GetInputConfig 返回后引擎即可回收）。
- 无入参脚本 / 普通 run_script 不受影响。
- `.custom` 传入非函数参数 → getInputConfig 求值报清晰错误 `NormalizedScale.custom argument "toValue" must be a function.`（菜单弹错、agent 回灌）。
