// 出错原子回退验证：先改一个音符（升八度），再抛错。期望：工程完全不变（改动被回退）、弹错误对话框。
function getScriptInfo() {
  return {
    name: 'Boom (rollback test)',
    category: 'Test',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'global',
  };
}

function main() {
  const part = tl.currentPart();
  if (part) {
    const notes = part.notes();
    if (notes.length > 0) notes[0].pitch += 12;   // 这步改动应被回退
  }
  throw new Error('boom — intentional error to test atomic rollback');
}
