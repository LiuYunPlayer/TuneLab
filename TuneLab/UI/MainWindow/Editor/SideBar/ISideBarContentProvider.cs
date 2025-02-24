using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.UI;

internal interface ISideBarContentProvider
{
    SideBar.SideBarContent Content { get; }
}
