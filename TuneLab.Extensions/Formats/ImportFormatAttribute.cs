using System;

namespace TuneLab.Extensions.Formats;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class ImportFormatAttribute : Attribute
{
    public string FileExtension { get; private set; }

    public ImportFormatAttribute(string fileExtension)
    {
        FileExtension = fileExtension;
    }
}
