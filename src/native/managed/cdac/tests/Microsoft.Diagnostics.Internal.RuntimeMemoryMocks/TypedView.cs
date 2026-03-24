// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public abstract class TypedView
{
    public ulong Address { get; private set; }

    public Memory<byte> Memory { get; private set; }

    public Layout Layout { get; private set; } = null!;

    protected MockTarget.Architecture Architecture { get; private set; }

    internal void Init(Memory<byte> memory, ulong address, Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfLessThan(memory.Length, layout.Size);

        Address = address;
        Memory = memory;
        Layout = layout;
        Architecture = layout.Architecture;
    }

    protected ulong ReadPointerField(string fieldName)
        => ReadPointer(GetFieldSlice(fieldName));

    protected uint ReadUInt32Field(string fieldName)
    {
        ReadOnlySpan<byte> source = GetFieldSlice(fieldName);
        return Architecture.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source)
            : BinaryPrimitives.ReadUInt32BigEndian(source);
    }

    protected int ReadInt32Field(string fieldName)
    {
        ReadOnlySpan<byte> source = GetFieldSlice(fieldName);
        return Architecture.IsLittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(source)
            : BinaryPrimitives.ReadInt32BigEndian(source);
    }

    protected long ReadInt64Field(string fieldName)
    {
        ReadOnlySpan<byte> source = GetFieldSlice(fieldName);
        return Architecture.IsLittleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(source)
            : BinaryPrimitives.ReadInt64BigEndian(source);
    }

    protected ulong ReadUInt64Field(string fieldName)
    {
        ReadOnlySpan<byte> source = GetFieldSlice(fieldName);
        return Architecture.IsLittleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(source)
            : BinaryPrimitives.ReadUInt64BigEndian(source);
    }

    protected void WritePointerField(string fieldName, ulong value)
        => WritePointer(GetFieldSlice(fieldName), value);

    protected void WriteUInt32Field(string fieldName, uint value)
        => WriteUInt32(GetFieldSlice(fieldName), value);

    protected void WriteInt32Field(string fieldName, int value)
        => WriteInt32(GetFieldSlice(fieldName), value);

    protected void WriteInt64Field(string fieldName, long value)
        => WriteInt64(GetFieldSlice(fieldName), value);

    protected void WriteUInt64Field(string fieldName, ulong value)
        => WriteUInt64(GetFieldSlice(fieldName), value);

    protected Span<byte> GetFieldSlice(string fieldName)
    {
        LayoutField field = Layout.GetField(fieldName);
        return Memory.Span.Slice(field.Offset, field.Size);
    }

    protected Memory<byte> GetFieldMemory(string fieldName)
    {
        LayoutField field = Layout.GetField(fieldName);
        return Memory.Slice(field.Offset, field.Size);
    }

    protected ulong GetFieldAddress(string fieldName)
        => Address + (ulong)Layout.GetField(fieldName).Offset;

    protected TView CreateFieldView<TView>(string fieldName)
        where TView : TypedView, new()
    {
        LayoutField layoutField = Layout.GetField(fieldName);
        Layout<TView> fieldLayout = (Layout<TView>)(layoutField.Type
            ?? throw new InvalidOperationException($"Field '{fieldName}' does not have a typed layout."));
        return fieldLayout.Create(GetFieldMemory(fieldName), GetFieldAddress(fieldName));
    }

    protected ulong ReadPointer(ReadOnlySpan<byte> source)
    {
        if (Architecture.Is64Bit)
        {
            return Architecture.IsLittleEndian
                ? BinaryPrimitives.ReadUInt64LittleEndian(source)
                : BinaryPrimitives.ReadUInt64BigEndian(source);
        }

        return Architecture.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source)
            : BinaryPrimitives.ReadUInt32BigEndian(source);
    }

    protected void WritePointer(Span<byte> destination, ulong value)
    {
        if (Architecture.Is64Bit)
        {
            if (Architecture.IsLittleEndian)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(destination, value);
            }

            return;
        }

        uint truncatedValue = unchecked((uint)value);
        if (Architecture.IsLittleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, truncatedValue);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, truncatedValue);
        }
    }

    protected void WriteUInt32(Span<byte> destination, uint value)
    {
        if (Architecture.IsLittleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        }
    }

    protected void WriteInt32(Span<byte> destination, int value)
    {
        if (Architecture.IsLittleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(destination, value);
        }
    }

    protected void WriteInt64(Span<byte> destination, long value)
    {
        if (Architecture.IsLittleEndian)
        {
            BinaryPrimitives.WriteInt64LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt64BigEndian(destination, value);
        }
    }

    protected void WriteUInt64(Span<byte> destination, ulong value)
    {
        if (Architecture.IsLittleEndian)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
        }
    }
}

public abstract class TypedArrayView
{
    public ulong Address { get; private set; }

    public Memory<byte> Memory { get; private set; }

    public Layout ElementLayout { get; private set; } = null!;

    protected MockTarget.Architecture Architecture { get; private set; }

    public int ElementCount { get; private set; }

    internal void Init(Memory<byte> memory, ulong address, Layout elementLayout, int elementCount)
    {
        ArgumentNullException.ThrowIfNull(elementLayout);
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(memory.Length, checked(elementLayout.Size * elementCount));

        Address = address;
        Memory = memory;
        ElementLayout = elementLayout;
        Architecture = elementLayout.Architecture;
        ElementCount = elementCount;
    }

    protected Span<byte> GetElementSlice(int elementIndex)
    {
        return GetElementMemory(elementIndex).Span;
    }

    protected int GetElementOffset(int elementIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(elementIndex, ElementCount);

        return checked(elementIndex * ElementLayout.Size);
    }

    protected ulong GetElementAddress(int elementIndex)
        => Address + (ulong)GetElementOffset(elementIndex);

    protected Memory<byte> GetElementMemory(int elementIndex)
        => Memory.Slice(GetElementOffset(elementIndex), ElementLayout.Size);

    protected Span<byte> GetElementFieldSlice(int elementIndex, string fieldName)
        => GetElementSlice(elementIndex).Slice(ElementLayout.GetField(fieldName).Offset);

    protected Span<byte> GetElementFieldSlice(int elementIndex, string fieldName, int length)
        => GetElementFieldSlice(elementIndex, fieldName).Slice(0, length);

    protected ulong ReadPointerField(int elementIndex, string fieldName)
        => ReadPointer(GetElementFieldSlice(elementIndex, fieldName, Architecture.PointerSize));

    protected void WritePointerField(int elementIndex, string fieldName, ulong value)
        => WritePointer(GetElementFieldSlice(elementIndex, fieldName, Architecture.PointerSize), value);

    protected uint ReadUInt32Field(int elementIndex, string fieldName)
    {
        ReadOnlySpan<byte> source = GetElementFieldSlice(elementIndex, fieldName, sizeof(uint));
        return Architecture.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source)
            : BinaryPrimitives.ReadUInt32BigEndian(source);
    }

    protected void WriteUInt32Field(int elementIndex, string fieldName, uint value)
        => WriteUInt32(GetElementFieldSlice(elementIndex, fieldName, sizeof(uint)), value);

    protected ulong ReadPointer(ReadOnlySpan<byte> source)
    {
        if (Architecture.Is64Bit)
        {
            return Architecture.IsLittleEndian
                ? BinaryPrimitives.ReadUInt64LittleEndian(source)
                : BinaryPrimitives.ReadUInt64BigEndian(source);
        }

        return Architecture.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source)
            : BinaryPrimitives.ReadUInt32BigEndian(source);
    }

    protected void WritePointer(Span<byte> destination, ulong value)
    {
        if (Architecture.Is64Bit)
        {
            if (Architecture.IsLittleEndian)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(destination, value);
            }

            return;
        }

        uint truncatedValue = unchecked((uint)value);
        if (Architecture.IsLittleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, truncatedValue);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, truncatedValue);
        }
    }

    protected void WriteUInt32(Span<byte> destination, uint value)
    {
        if (Architecture.IsLittleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        }
    }
}

public class TypedArrayView<TElementView> : TypedArrayView
    where TElementView : TypedView, new()
{
    public new Layout<TElementView> ElementLayout => (Layout<TElementView>)base.ElementLayout;

    public TElementView GetElement(int elementIndex)
        => ElementLayout.Create(GetElementMemory(elementIndex), GetElementAddress(elementIndex));
}
