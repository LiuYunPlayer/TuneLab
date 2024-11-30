﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Document;

public class UndoOnlyCommand(Action undo) : ICommand
{
    public void Redo()
    {

    }

    public void Undo()
    {
        undo();
    }
}
