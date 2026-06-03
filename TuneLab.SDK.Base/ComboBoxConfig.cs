using System.Collections.Generic;
using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// 原 EnumConfig（按 UI 控件命名）。
public class ComboBoxConfig(IReadOnlyList<string> options, int defaultIndex = 0) : IValueConfig<string>
{
    public int DefaultIndex { get; set; } = defaultIndex;
    public IReadOnlyList<string> Options { get; set; } = options;
    public string DefaultValue
    {
        get => (uint)DefaultIndex < Options.Count ? Options[DefaultIndex] : string.Empty;
        set
        {
            int index = -1;
            for (int i = 0; i < Options.Count; i++)
            {
                if (Options[i] == value)
                {
                    index = i;
                    break;
                }
            }
            DefaultIndex = index;
        }
    }
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
