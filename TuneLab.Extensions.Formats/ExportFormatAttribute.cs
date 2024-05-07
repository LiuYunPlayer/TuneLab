using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Formats;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class ExportFormatAttribute : Attribute
{
    public string FileExtension { get; private set; }

    public ExportFormatAttribute(string fileExtension)
    {
        FileExtension = fileExtension;
    }
}
