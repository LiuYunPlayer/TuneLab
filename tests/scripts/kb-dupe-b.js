// 快捷键测试：声明 id 碰撞（忠实降级回文件名）。见 kb-dupe-a.js。两者都声明 id='kb.dupe'。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? 'KB 重复 id B' : 'KB Dupe Id B',
    category: zh ? '快捷键测试' : 'Keybinding Test',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'global',
    id: 'kb.dupe',
  };
}

function main() {
  print('kb-dupe-b ran');
}
