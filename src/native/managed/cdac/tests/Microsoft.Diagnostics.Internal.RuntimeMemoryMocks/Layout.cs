// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public class Layout
{
    public Layout(string name, MockTarget.Architecture architecture, int size, LayoutField[] fields, ulong? alignment = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (alignment.HasValue && ((alignment.Value & (alignment.Value - 1)) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a power of two.");
        }

        Name = name;
        Architecture = architecture;
        Size = size;
        Fields = fields;
        Alignment = alignment;
    }

    public string Name { get; }

    public MockTarget.Architecture Architecture { get; }

    public int Size { get; }

    public LayoutField[] Fields { get; }

    public ulong? Alignment { get; }

    internal LayoutField GetField(string fieldName)
    {
        foreach (LayoutField field in Fields)
        {
            if (field.Name == fieldName)
            {
                return field;
            }
        }

        throw new InvalidOperationException($"Field '{fieldName}' not found.");
    }

}

public sealed class Layout<TView> : Layout
    where TView : TypedView, new()
{
    public Layout(string name, MockTarget.Architecture architecture, int size, LayoutField[] fields, ulong? alignment = null)
        : base(name, architecture, size, fields, alignment)
    {
    }

    public TView Create(MockMemorySpace.HeapFragment fragment)
    {
        TView view = new();
        view.Init(fragment.Data.AsMemory(), fragment.Address, this);
        return view;
    }

    public TView Create(Memory<byte> memory, ulong address)
    {
        TView view = new();
        view.Init(memory, address, this);
        return view;
    }

    public TView Allocate(MockMemorySpace.BumpAllocator allocator, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(allocator);

        return Create(allocator.AllocateFragment((ulong)Size, name, Alignment));
    }

    public TArrayView CreateCustomArray<TArrayView>(MockMemorySpace.HeapFragment fragment)
        where TArrayView : TypedArrayView, new()
        => CreateCustomArray<TArrayView>(fragment.Data.AsMemory(), fragment.Address);

    public TArrayView CreateCustomArray<TArrayView>(Memory<byte> memory, ulong address)
        where TArrayView : TypedArrayView, new()
    {
        TArrayView view = new();
        view.Init(memory, address, this, GetElementCount(memory.Length));
        return view;
    }

    public TArrayView AllocateCustomArray<TArrayView>(MockMemorySpace.BumpAllocator allocator, int elementCount, string? name = null)
        where TArrayView : TypedArrayView, new()
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);

        return CreateCustomArray<TArrayView>(allocator.AllocateFragment(checked((ulong)Size * (ulong)elementCount), name, Alignment));
    }

    public TypedArrayView<TView> CreateArray(MockMemorySpace.HeapFragment fragment)
        => CreateCustomArray<TypedArrayView<TView>>(fragment);

    public TypedArrayView<TView> CreateArray(Memory<byte> memory, ulong address)
        => CreateCustomArray<TypedArrayView<TView>>(memory, address);

    public TypedArrayView<TView> AllocateArray(MockMemorySpace.BumpAllocator allocator, int elementCount, string? name = null)
        => AllocateCustomArray<TypedArrayView<TView>>(allocator, elementCount, name);

    private int GetElementCount(int byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);

        if (Size == 0)
        {
            throw new InvalidOperationException("Cannot create an array view for a zero-sized layout.");
        }

        if ((byteLength % Size) != 0)
        {
            throw new ArgumentException("Array memory length must be a multiple of the element size.", nameof(byteLength));
        }

        return byteLength / Size;
    }
}

public readonly record struct LayoutField(string Name, int Offset, int Size, Layout? Type = null);

public sealed class LayoutBuilder
{
    private readonly string _name;
    private readonly MockTarget.Architecture _architecture;
    private readonly Dictionary<string, LayoutField> _fields = new(StringComparer.Ordinal);

    public LayoutBuilder(string name, MockTarget.Architecture architecture)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _name = name;
        _architecture = architecture;
    }

    public int Size { get; set; }

    public ulong? Alignment { get; private set; }

    public LayoutBuilder AddField(string name, int offset, int size, Layout? type = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        _fields[name] = new LayoutField(name, offset, size, type);
        return this;
    }

    public LayoutBuilder SetAlignment(ulong? alignment)
    {
        if (!alignment.HasValue || alignment.Value <= 1)
        {
            Alignment = null;
            return this;
        }

        if ((alignment.Value & (alignment.Value - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a power of two.");
        }

        Alignment = alignment;
        return this;
    }

    public Layout Build()
        => new(_name, _architecture, Size, [.. _fields.Values], Alignment);

    public Layout<TView> Build<TView>()
        where TView : TypedView, new()
        => new(_name, _architecture, Size, [.. _fields.Values], Alignment);
}

public sealed class SequentialLayoutBuilder
{
    private readonly LayoutBuilder _layoutBuilder;
    private readonly MockTarget.Architecture _architecture;
    private int _currentSize;

    public SequentialLayoutBuilder(string name, MockTarget.Architecture architecture)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        _layoutBuilder = new LayoutBuilder(name, architecture);
        _architecture = architecture;
    }

    public int Size => _currentSize;

    public SequentialLayoutBuilder AddField(string name, int size, Layout? type = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        int alignment = Math.Min(size, _architecture.PointerSize);
        _currentSize = AlignUp(_currentSize, alignment);
        _layoutBuilder.AddField(name, _currentSize, size, type);
        _currentSize += size;
        _layoutBuilder.Size = _currentSize;
        return this;
    }

    public SequentialLayoutBuilder SetAlignment(ulong? alignment)
    {
        _layoutBuilder.SetAlignment(alignment);
        return this;
    }

    public SequentialLayoutBuilder AddInt32Field(string name)
        => AddField(name, sizeof(int));

    public SequentialLayoutBuilder AddUInt32Field(string name)
        => AddField(name, sizeof(uint));

    public SequentialLayoutBuilder AddInt64Field(string name)
        => AddField(name, sizeof(long));

    public SequentialLayoutBuilder AddPointerField(string name)
        => AddField(name, _architecture.PointerSize);

    public SequentialLayoutBuilder AddNUIntField(string name)
        => AddField(name, _architecture.PointerSize);

    public Layout Build() => _layoutBuilder.Build();

    public Layout<TView> Build<TView>()
        where TView : TypedView, new()
        => _layoutBuilder.Build<TView>();

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);
}
