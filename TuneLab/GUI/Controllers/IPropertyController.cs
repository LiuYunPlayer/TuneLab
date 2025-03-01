namespace TuneLab.GUI.Controllers;

internal interface IPropertyController
{
    void Terminate();
}

internal interface IPropertyController<T> : IPropertyController
{
    void Setup(T value);
}
