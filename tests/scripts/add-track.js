// 顶部菜单工具（context=global）：新增一条空轨道。
// （track 类 context 待轨道头交互升级后再做，故这里归到全局 Scripts 菜单。）
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '新增空轨道' : 'Add Empty Track',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'global',
  };
}

function main() {
  const t = tl.currentProject().addTrack('Script Track');
  print('added track: ' + t.name);
}
