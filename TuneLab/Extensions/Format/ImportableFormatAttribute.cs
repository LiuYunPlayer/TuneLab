using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Format;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ImportableFormatAttribute(string fileExtension) : Attribute
{
    public string FileExtension { get; private set; } = fileExtension;
}
