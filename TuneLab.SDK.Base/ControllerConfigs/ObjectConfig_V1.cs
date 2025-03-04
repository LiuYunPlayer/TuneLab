namespace TuneLab.SDK.Base;

public class ObjectConfig_V1 : IControllerConfig_V1
{
    public OrderedMap_V1<string, IControllerConfig_V1> Configs { get; set; } = [];
    PropertyValue_V1 IControllerConfig_V1.DefaultValue
    {
        get
        {
            PropertyObject_V1 propertyObject = [];
            foreach (var config in Configs)
            {
                propertyObject.Add(config.Key, config.Value.DefaultValue);
            }
            return propertyObject;
        }
    }
}
