using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Data;

public interface ICommand
{
    public void Undo();
    public void Redo();
}
