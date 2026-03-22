// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public class MockMemoryHelpers
{
    public MockTarget.Architecture Arch { get; }

    public MockMemoryHelpers(MockTarget.Architecture arch)
    {
        Arch = arch;
    }

    public int PointerSize => Arch.Is64Bit ? sizeof(ulong) : sizeof(uint);
    public ulong MaxSignedTargetAddress => (ulong)(Arch.Is64Bit ? long.MaxValue : int.MaxValue);

    internal uint ObjHeaderSize => (uint)(Arch.Is64Bit ? 2 * sizeof(uint) : sizeof(uint));
    internal uint ObjectSize => (uint)PointerSize;
    internal uint ObjectBaseSize => ObjHeaderSize + ObjectSize;
    internal uint ArrayBaseSize => Arch.Is64Bit ? ObjectSize + sizeof(uint) + sizeof(uint) : ObjectSize + sizeof(uint);
    internal uint ArrayBaseBaseSize => ObjHeaderSize + ArrayBaseSize;
    internal uint StringBaseSize => ObjectBaseSize + sizeof(uint) + sizeof(char);

    internal void Write(Span<byte> dest, byte value)
    {
        _ = Arch;
        dest[0] = value;
    }

    internal void Write(Span<byte> dest, ushort value)
    {
        if (Arch.IsLittleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(dest, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(dest, value);
        }
    }

    internal void Write(Span<byte> dest, int value)
    {
        if (Arch.IsLittleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(dest, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(dest, value);
        }
    }

    internal void Write(Span<byte> dest, uint value)
    {
        if (Arch.IsLittleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(dest, value);
        }
    }

    internal void Write(Span<byte> dest, ulong value)
    {
        if (Arch.IsLittleEndian)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(dest, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(dest, value);
        }
    }

    internal void WritePointer(Span<byte> dest, ulong value)
    {
        if (Arch.Is64Bit)
        {
            Write(dest, value);
        }
        else
        {
            Write(dest, (uint)value);
        }
    }

    internal void WriteUtf16String(Span<byte> dest, string value)
    {
        Encoding encoding = Arch.IsLittleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        byte[] valueBytes = encoding.GetBytes(value);
        int requiredLength = valueBytes.Length + sizeof(char);
        if (dest.Length < requiredLength)
        {
            throw new InvalidOperationException($"Destination is too short to write '{value}'. Required length: {requiredLength}, actual: {dest.Length}");
        }

        valueBytes.AsSpan().CopyTo(dest);
        dest[^2] = 0;
        dest[^1] = 0;
    }
}
