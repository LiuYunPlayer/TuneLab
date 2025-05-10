using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Property;

public interface IReadOnlyPropertyObject : IReadOnlyPropertyValue
{
    PropertyType IReadOnlyPropertyValue.Type => PropertyType.Object;
}
