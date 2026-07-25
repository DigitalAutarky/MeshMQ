using System.Runtime.CompilerServices;

namespace HackyMessage.Persistence.Provider.Disk;

public readonly record struct None();
public readonly record struct Item<T>(T item, long readPosition);

//TODO: use proper union declaration once supported by rider..
[Union]
public readonly struct ItemOrNone<T> : IUnion
{
    public bool HasValue => true;
    private readonly None _none = default;
    private readonly Item<T> _item = default;
    
    public object Value => _item != default ? _item : _none;
    
    public ItemOrNone(None none) => _none = none;
    public ItemOrNone(Item<T> item) => _item = item;

    public bool TryGetValue(out None value)
    {
        value = _none;
        return _none != default;
    }
    
    public bool TryGetValue(out Item<T> value)
    {
        value = _item;
        return _item != default;
    }
}