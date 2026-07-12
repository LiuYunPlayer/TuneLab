// 快捷键测试：建议默认手势撞内建（绝不静默夺键）。声明 defaultGesture='mod+s'，与内建 file.save 同手势同作用域。
// 期望：本脚本命令留「未绑定」+ 日志告警，Ctrl+S/⌘S 仍触发保存（脚本抢不走）。用户可在设置页手动改派。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? 'KB 撞保存键' : 'KB Save Clash',
    category: zh ? '快捷键测试' : 'Keybinding Test',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'global',
    defaultGesture: 'mod+s',
  };
}

function main() {
  print('kb-save-clash ran');
}
