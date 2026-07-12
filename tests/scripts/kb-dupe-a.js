// 快捷键测试：声明 id 碰撞（忠实降级回文件名）。本文件与 kb-dupe-b.js 都声明 id='kb.dupe'。
// 期望：两者都不用 script:kb.dupe，各自回落 script:kb-dupe-a / script:kb-dupe-b（日志告警），仍可独立绑定。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? 'KB 重复 id A' : 'KB Dupe Id A',
    category: zh ? '快捷键测试' : 'Keybinding Test',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'global',
    id: 'kb.dupe',
  };
}

function main() {
  print('kb-dupe-a ran');
}
