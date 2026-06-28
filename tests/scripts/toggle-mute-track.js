// 轨道头右键菜单工具（context=track，整轨对象）：切换选中轨道的静音。
// 右键轨道头时该轨必被选中，故目标 = selectedTracks()（支持多选）。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '切换静音' : 'Toggle Mute',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'track',
  };
}

function main() {
  const tracks = tl.selectedTracks();
  for (const t of tracks) t.isMute = !t.isMute;
  print('toggled mute on ' + tracks.length + ' track(s)');
}
