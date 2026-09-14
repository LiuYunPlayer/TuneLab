# Py Format

脚本 format 插件的测试样例：`.pyt` = 一行一个音符的文本（`pitch pos dur lyric`）。

- 联调（宿主测试 `ScriptedPythonE2ETests` 自动做）：`PYTHONPATH` 指 `scripting/python`，
  本目录作为包目录直接 `ExtensionManager.Load`。
- 真实安装体验：`pwsh tests/pack-tlx.ps1; pwsh tests/install-tlx.ps1 PyFormat` → 拖入宿主
  （无 csproj 的 `tests/plugins/*` 目录被 pack 脚本直接收编，免构建）。
- 发布给自己的用户时，作者应 `pip install --target=plugin/vendor tunelab`（vendoring 纪律，
  设计文档 §12.2），本测试样例免此步靠 PYTHONPATH。
