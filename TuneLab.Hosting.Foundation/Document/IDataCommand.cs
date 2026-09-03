using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

// 撤销栈里的一次可撤销突变（DataDocument 存的就是它）。前缀 Data 点明所属层——
// 与输入层的 KeyCommand（可绑快捷键的 UI 命令）不是一回事。
public interface IDataCommand
{
    public void Undo();
    public void Redo();
}
