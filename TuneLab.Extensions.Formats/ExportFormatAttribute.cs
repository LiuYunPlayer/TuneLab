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
