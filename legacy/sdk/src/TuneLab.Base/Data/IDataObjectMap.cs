using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Data;

internal interface IDataObjectMap<TKey, TValue> : IDataMap<TKey, TValue>, IReadOnlyDataObjectMap<TKey, TValue> where TKey : notnull where TValue : IDataObject
{

}
