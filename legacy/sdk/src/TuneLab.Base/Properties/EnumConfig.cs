using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;


namespace TuneLab.Base.Properties;

public class EnumConfig(IReadOnlyList<string> options, int defaultIndex = 0) : IValueConfig<string>
{
    public int DefaultIndex { get; set; } = defaultIndex;
    public IReadOnlyList<string> Options { get; set; } = options;
    public string DefaultValue
    {
        get => (uint)DefaultIndex < Options.Count ? Options[DefaultIndex] : string.Empty;
        set { DefaultIndex = Options.IndexOf(value); }
    }
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
