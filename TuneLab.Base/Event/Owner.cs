namespace TuneLab.Base.Event;

public class Owner<T> : IProvider<T> where T : class
{
    public IActionEvent ObjectWillChange => mObjectWillChanged;
    public IActionEvent ObjectChanged => mObjectChanged;
    public T? Object
    {
        get => mObject;
        set => Set(value);
    }

    public static implicit operator T?(Owner<T> owner)
    {
        return owner.Object;
    }

    public void Set(T? newDataObject)
    {
        if (mObject == newDataObject)
            return;

        mObjectWillChanged.Invoke();
        mObject = newDataObject;
        mObjectChanged.Invoke();
    }

    T? mObject;
    readonly ActionEvent mObjectWillChanged = new();
    readonly ActionEvent mObjectChanged = new();
}
