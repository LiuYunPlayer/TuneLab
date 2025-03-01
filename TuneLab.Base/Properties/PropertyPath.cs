namespace TuneLab.Base.Properties;

public readonly struct PropertyPath
{
    public static bool operator ==(PropertyPath left, PropertyPath right)
    {
        return left == right.mIDs;
    }

    public static bool operator ==(PropertyPath propertyPath, IReadOnlyList<string> IDs)
    {
        if (propertyPath.mIDs.Count != IDs.Count)
            return false;

        for (int i = 0; i < IDs.Count; i++)
        {
            if (propertyPath.mIDs[i] != IDs[i])
                return false;
        }

        return true;
    }

    public static bool operator !=(PropertyPath left, PropertyPath right)
    {
        return !(left == right);
    }

    public static bool operator !=(PropertyPath propertyPath, IReadOnlyList<string> IDs)
    {
        return !(propertyPath.mIDs == IDs);
    }

    public PropertyPath()
    {

    }

    public PropertyPath(IEnumerable<string> keys)
    {
        mIDs.AddRange(keys);
    }

    public PropertyPath(params string[] keys)
    {
        mIDs.AddRange(keys);
    }

    public readonly struct Key
    {
        public static implicit operator string(Key key)
        {
            return key.Name;
        }

        public Key(PropertyPath path) : this(path, 0) { }

        public bool IsObject => mIndex < mPath.mIDs.Count - 1;
        public string Name => mPath.mIDs[mIndex];
        public Key Next => new Key(mPath, mIndex + 1);

        Key(PropertyPath path, int index)
        {
            mPath = path;
            mIndex = index;
        }

        readonly PropertyPath mPath;
        readonly int mIndex;
    }

    public PropertyPath Combine(PropertyPath path)
    {
        return Combine(path.mIDs);
    }

    public PropertyPath Combine(params string[] keys)
    {
        return new PropertyPath(mIDs.Concat(keys));
    }

    public PropertyPath Combine(IEnumerable<string> keys)
    {
        return new PropertyPath(mIDs.Concat(keys));
    }

    public PropertyPath TrimRoot()
    {
        return new PropertyPath(mIDs[1..]);
    }

    public Key GetKey()
    {
        return new Key(this);
    }

    readonly List<string> mIDs = new();
}
