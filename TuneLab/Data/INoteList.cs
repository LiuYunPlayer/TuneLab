using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;

namespace TuneLab.Data;

internal interface INoteList : IReadOnlyDataObjectLinkedList<INote>, ISelectableCollection<INote>
{

}
