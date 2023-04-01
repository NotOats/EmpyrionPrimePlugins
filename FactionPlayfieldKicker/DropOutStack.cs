using System.Collections;

namespace FactionPlayfieldKicker;

/// <summary>
/// Simplistic drop out stack. Semi thread safe
/// </summary>
/// <typeparam name="T"></typeparam>
internal class DropOutStack<T> : IEnumerable<T>
{
    private readonly object _itemsLock = new();
    private readonly T[] _items;

    private int _top;
    private int _count;

    public DropOutStack(int capacity)
    {
        _items = new T[capacity];
    }

    public void Push(T item)
    {
        lock (_itemsLock)
        {
            _count++;
            _count = _count > _items.Length ? _items.Length : _count;

            _items[_top] = item;
            _top = (_top + 1) % _items.Length;
        }
    }

    public T Pop()
    {
        lock (_itemsLock)
        {
            _count--;
            _count = _count < 0 ? 0 : _count;

            _top = (_items.Length + _top - 1) % _items.Length;
            return _items[_top];
        }
    }

    public T Peek()
    {
        lock (_itemsLock)
        {
            var top = (_items.Length + _top - 1) % _items.Length;
            return _items[top];
        }
    }

    public int Count() => _count;

    public void Clear()
    { 
        lock (_itemsLock)
        {
            _count = 0;
        }
    }

    public T GetItem(int index)
    {
        lock(_itemsLock)
        {
            if (index > Count())
                throw new InvalidOperationException("Index out of bounds");

            return _items[(_items.Length + _top - (index + 1)) % _items.Length];
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        lock(_itemsLock)
        {
            for (int i = 0; i < Count(); i++)
            {
                yield return GetItem(i);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
