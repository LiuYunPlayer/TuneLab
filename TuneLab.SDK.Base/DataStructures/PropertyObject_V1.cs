using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base.DataStructures;

public class PropertyObject_V1 : Dictionary<string, PropertyValue_V1>
{
    public bool GetBool(string key, bool defaultValue = false) => TryGetValue(key, out var value) ? value.ToBool(out var result) ? result : defaultValue : defaultValue;
    public PropertyNumber_V1 GetNumber(string key, PropertyNumber_V1 defaultValue = default) => TryGetValue(key, out var value) ? value.ToNumber(out var result) ? result : defaultValue : defaultValue;
    public string GetString(string key, string defaultValue = "") => TryGetValue(key, out var value) ? value.ToString(out var result) ? result : defaultValue : defaultValue;
    public PropertyObject_V1 GetObject(string key, PropertyObject_V1 defaultValue) => TryGetValue(key, out var value) ? value.ToObject(out var result) ? result : defaultValue : defaultValue;
    public PropertyObject_V1 GetObject(string key) => TryGetValue(key, out var value) ? value.ToObject(out var result) ? result : [] : [];
}
