using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

public interface IDataObjectList<T> : IDataList<T>, IReadOnlyDataObjectList<T> where T : IDataObject
{

}
