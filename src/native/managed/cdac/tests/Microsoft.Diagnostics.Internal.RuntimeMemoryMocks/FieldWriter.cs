// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

internal ref struct FieldWriter
{
    private readonly Span<byte> _buffer;
    private readonly MockTarget.Architecture _architecture;
    private readonly MockDataDescriptorType _type;

    public FieldWriter(Span<byte> buffer, MockTarget.Architecture architecture, MockDataDescriptorType type)
    {
        _buffer = buffer;
        _architecture = architecture;
        _type = type;
    }

    public void WriteInt32Field(string fieldName, int value)
    {
        SpanWriter writer = new(_architecture, GetFieldSlice(fieldName, sizeof(int)));
        writer.Write(value);
    }

    public void WriteUInt32Field(string fieldName, uint value)
    {
        SpanWriter writer = new(_architecture, GetFieldSlice(fieldName, sizeof(uint)));
        writer.Write(value);
    }

    public void WriteInt64Field(string fieldName, long value)
    {
        SpanWriter writer = new(_architecture, GetFieldSlice(fieldName, sizeof(long)));
        writer.Write(unchecked((ulong)value));
    }

    public void WritePointerField(string fieldName, ulong pointerValue)
    {
        SpanWriter writer = new(_architecture, GetFieldSlice(fieldName, _architecture.PointerSize));
        writer.WritePointer(pointerValue);
    }

    public void WriteNUIntField(string fieldName, ulong value)
        => WritePointerField(fieldName, value);

    public void WriteField(string fieldName, ReadOnlySpan<byte> content)
        => content.CopyTo(GetFieldSlice(fieldName, content.Length));

    public Span<byte> GetFieldSlice(string fieldName)
        => _buffer.Slice(_type.GetFieldOffset(fieldName));

    private Span<byte> GetFieldSlice(string fieldName, int length)
        => GetFieldSlice(fieldName).Slice(0, length);
}
