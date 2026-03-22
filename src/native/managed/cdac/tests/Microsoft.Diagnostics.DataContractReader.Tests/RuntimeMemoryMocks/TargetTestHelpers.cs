// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.DataContractReader;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public unsafe class TargetTestHelpers : MockMemoryHelpers
{
    public TargetTestHelpers(MockTarget.Architecture arch)
        : base(arch)
    {
    }

    internal void WriteNUInt(Span<byte> dest, TargetNUInt targetNUInt) => WritePointer(dest, targetNUInt.Value);

    internal TargetPointer ReadPointer(ReadOnlySpan<byte> src)
    {
        if (Arch.Is64Bit)
        {
            return Arch.IsLittleEndian
                ? BitConverter.ToUInt64(src)
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(src);
        }

        return Arch.IsLittleEndian
            ? BitConverter.ToUInt32(src)
            : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(src);
    }

    internal int SizeOfPrimitive(DataType type)
    {
        return type switch
        {
            DataType.uint8 or DataType.int8 => sizeof(byte),
            DataType.uint16 or DataType.int16 => sizeof(ushort),
            DataType.uint32 or DataType.int32 => sizeof(uint),
            DataType.uint64 or DataType.int64 => sizeof(ulong),
            DataType.pointer or DataType.nint or DataType.nuint => PointerSize,
            _ => throw new InvalidOperationException($"Not a primitive: {type}"),
        };
    }

    internal int SizeOfTypeInfo(Target.TypeInfo info)
    {
        int size = 0;
        foreach (var (_, field) in info.Fields)
        {
            size = Math.Max(size, field.Offset + SizeOfPrimitive(field.Type));
        }

        return size;
    }

    private static int AlignUp(int offset, int align) => (offset + align - 1) & ~(align - 1);

    public enum FieldLayout
    {
        CIsh,
        Packed,
    }

    public readonly struct LayoutResult
    {
        public Dictionary<string, Target.FieldInfo> Fields { get; init; }
        public uint Stride { get; init; }
        public uint MaxAlign { get; init; }
    }

    public record Field(string Name, DataType Type, uint? Size = null);

    public LayoutResult LayoutFields(Field[] fields) => LayoutFields(FieldLayout.CIsh, fields);

    public LayoutResult LayoutFields(FieldLayout style, Field[] fields)
    {
        int offset = 0;
        int maxAlign = 1;
        return LayoutFieldsWorker(style, fields, ref offset, ref maxAlign);
    }

    private LayoutResult LayoutFieldsWorker(FieldLayout style, Field[] fields, ref int offset, ref int maxAlign)
    {
        Dictionary<string, Target.FieldInfo> fieldInfos = new();

        for (int i = 0; i < fields.Length; i++)
        {
            var (name, type, sizeMaybe) = fields[i];
            int size = sizeMaybe.HasValue ? (int)sizeMaybe.Value : SizeOfPrimitive(type);
            int align = size;
            if (align > maxAlign)
            {
                maxAlign = align;
            }

            offset = style switch
            {
                FieldLayout.CIsh => AlignUp(offset, align),
                FieldLayout.Packed => offset,
                _ => throw new InvalidOperationException("Unknown layout style"),
            };

            fieldInfos[name] = new Target.FieldInfo
            {
                Offset = offset,
                Type = type,
            };

            offset += size;
        }

        int stride = style switch
        {
            FieldLayout.CIsh => AlignUp(offset, maxAlign),
            FieldLayout.Packed => offset,
            _ => throw new InvalidOperationException("Unknown layout style"),
        };

        return new LayoutResult { Fields = fieldInfos, Stride = (uint)stride, MaxAlign = (uint)maxAlign };
    }

    public LayoutResult ExtendLayout(Field[] fields, LayoutResult baseClass) => ExtendLayout(FieldLayout.CIsh, fields, baseClass);

    public LayoutResult ExtendLayout(FieldLayout fieldLayout, Field[] fields, LayoutResult baseClass)
    {
        int offset = (int)baseClass.Stride;
        int maxAlign = (int)baseClass.MaxAlign;
        return LayoutFieldsWorker(fieldLayout, fields, ref offset, ref maxAlign);
    }
}
