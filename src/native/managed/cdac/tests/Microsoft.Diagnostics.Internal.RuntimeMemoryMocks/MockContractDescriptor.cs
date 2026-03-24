// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public static class MockDataDescriptorModuleExtensions
{
    private static MockDataDescriptorBuilder GetOrCreateDataDescriptorBuilder(this MockModuleBuilder moduleBuilder)
    {
        MockDataDescriptorBuilder descriptorBuilder;
        if (moduleBuilder.Services.TryGetValue(typeof(MockDataDescriptorBuilder), out object? descriptorBuilderObject))
        {
            descriptorBuilder = (MockDataDescriptorBuilder)descriptorBuilderObject;
        }
        else
        {
            descriptorBuilder = new MockDataDescriptorBuilder();
            moduleBuilder.Services.Add(typeof(MockDataDescriptorBuilder), descriptorBuilder);
            moduleBuilder.OnBuild(allocator =>
            {
                MockDataDescriptor descriptorData = descriptorBuilder.Build();
                ulong descriptorAddress = MockDataDescriptorSerializer.CreateDescriptor(
                    moduleBuilder.Architecture,
                    allocator,
                    descriptorData,
                    descriptorBuilder.ExportName);
                moduleBuilder.AddExport(descriptorBuilder.ExportName, descriptorAddress);
            });
        }

        descriptorBuilder.PointerSize = moduleBuilder.Architecture.PointerSize;
        return descriptorBuilder;
    }

    public static MockModuleBuilder AddDataDescriptor(this MockModuleBuilder moduleBuilder, Action<MockDataDescriptorBuilder> configure)
    {
        MockDataDescriptorBuilder descriptorBuilder = moduleBuilder.GetOrCreateDataDescriptorBuilder();
        configure(descriptorBuilder);
        return moduleBuilder;
    }
}

public sealed class MockDataDescriptorTypeBuilder
{
    private readonly Dictionary<string, MockDataDescriptorField> _fields = new(StringComparer.Ordinal);

    public uint? Size { get; set; }

    internal MockDataDescriptorTypeBuilder()
    {
    }

    internal MockDataDescriptorTypeBuilder(MockDataDescriptorType type)
    {
        Size = type.Size;
        foreach (MockDataDescriptorField field in type.Fields)
        {
            _fields[field.Name] = field;
        }
    }

    public MockDataDescriptorTypeBuilder AddField(string name, int offset, string? typeName = null)
    {
        _fields[name] = new MockDataDescriptorField(name, offset, typeName);
        return this;
    }

    internal int GetFieldOffset(string fieldName)
    {
        if (_fields.TryGetValue(fieldName, out MockDataDescriptorField field))
        {
            return field.Offset;
        }

        throw new InvalidOperationException($"Field '{fieldName}' not found.");
    }

    internal MockDataDescriptorType Build()
    {
        return new MockDataDescriptorType
        {
            Size = Size,
            Fields = [.. _fields.Values],
        };
    }
}

public sealed class MockDataDescriptorSequentialTypeBuilder
{
    private readonly MockDataDescriptorTypeBuilder _typeBuilder;
    private readonly int _pointerSize;
    private int _currentSize;

    internal MockDataDescriptorSequentialTypeBuilder(int pointerSize)
    {
        if (pointerSize <= 0)
        {
            throw new InvalidOperationException("Pointer size must be set before adding sequential types.");
        }

        _typeBuilder = new MockDataDescriptorTypeBuilder();
        _pointerSize = pointerSize;
    }

    internal MockDataDescriptorSequentialTypeBuilder(int pointerSize, MockDataDescriptorType type)
    {
        if (pointerSize <= 0)
        {
            throw new InvalidOperationException("Pointer size must be set before adding sequential types.");
        }

        _typeBuilder = new MockDataDescriptorTypeBuilder(type);
        _pointerSize = pointerSize;
        _currentSize = checked((int)(type.Size ?? 0));
    }

    public uint Size => checked((uint)_currentSize);

    public MockDataDescriptorSequentialTypeBuilder AddField(string name, int size, string? typeName = null)
    {
        if (size <= 0)
        {
            throw new InvalidOperationException($"Field '{name}' size must be positive.");
        }

        int alignment = Math.Min(size, _pointerSize);
        _currentSize = AlignUp(_currentSize, alignment);
        _typeBuilder.AddField(name, _currentSize, typeName);
        _currentSize += size;
        _typeBuilder.Size = checked((uint)_currentSize);
        return this;
    }

    public MockDataDescriptorSequentialTypeBuilder AddInt32Field(string name)
        => AddField(name, sizeof(int));

    public MockDataDescriptorSequentialTypeBuilder AddUInt32Field(string name)
        => AddField(name, sizeof(uint));

    public MockDataDescriptorSequentialTypeBuilder AddInt64Field(string name)
        => AddField(name, sizeof(long));

    public MockDataDescriptorSequentialTypeBuilder AddPointerField(string name)
        => AddField(name, _pointerSize);

    public MockDataDescriptorSequentialTypeBuilder AddNUIntField(string name)
        => AddField(name, _pointerSize);

    internal MockDataDescriptorType Build() => _typeBuilder.Build();

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);
}

public sealed class MockDataDescriptorBuilder
{
    private const string DefaultExportName = "DotNetRuntimeContractDescriptor";

    private readonly Dictionary<string, int> _contracts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MockDataDescriptorType> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MockDataDescriptorGlobal> _globals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MockSubDataDescriptor> _subDescriptors = new(StringComparer.Ordinal);
    private readonly List<ulong> _indirectValues = [];

    public string ExportName { get; set; } = DefaultExportName;
    internal int PointerSize { get; set; }

    public MockDataDescriptorBuilder AddContract(string name, int version)
    {
        _contracts[name] = version;
        return this;
    }

    public MockDataDescriptorType AddType(string name)
    {
        if (!_types.TryGetValue(name, out MockDataDescriptorType? type))
        {
            type = new MockDataDescriptorType();
            _types.Add(name, type);
        }

        return type;
    }

    public MockDataDescriptorType AddType(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return _types[layout.Name] = new MockDataDescriptorType(layout);
    }

    public MockDataDescriptorType AddType(string name, Action<MockDataDescriptorTypeBuilder> configure)
    {
        MockDataDescriptorTypeBuilder typeBuilder = _types.TryGetValue(name, out MockDataDescriptorType? type)
            ? new MockDataDescriptorTypeBuilder(type)
            : new MockDataDescriptorTypeBuilder();

        configure(typeBuilder);
        MockDataDescriptorType updatedType = typeBuilder.Build();
        _types[name] = updatedType;
        return updatedType;
    }

    public MockDataDescriptorType AddType(Layout layout, Action<MockDataDescriptorTypeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(layout);
        MockDataDescriptorTypeBuilder typeBuilder = new(new MockDataDescriptorType(layout));
        configure(typeBuilder);
        MockDataDescriptorType updatedType = typeBuilder.Build();
        _types[layout.Name] = updatedType;
        return updatedType;
    }

    public MockDataDescriptorType AddSequentialType(string name, Action<MockDataDescriptorSequentialTypeBuilder> configure)
    {
        MockDataDescriptorSequentialTypeBuilder typeBuilder = _types.TryGetValue(name, out MockDataDescriptorType? type)
            ? new MockDataDescriptorSequentialTypeBuilder(PointerSize, type)
            : new MockDataDescriptorSequentialTypeBuilder(PointerSize);

        configure(typeBuilder);
        MockDataDescriptorType updatedType = typeBuilder.Build();
        _types[name] = updatedType;
        return updatedType;
    }

    public MockDataDescriptorBuilder AddGlobalValue(string name, ulong value, string? typeName = null)
    {
        _globals[name] = new MockDataDescriptorGlobal(name, Value: value, TypeName: typeName);
        return this;
    }

    public MockDataDescriptorBuilder AddGlobalString(string name, string value, string? typeName = null)
    {
        _globals[name] = new MockDataDescriptorGlobal(name, StringValue: value, TypeName: typeName);
        return this;
    }

    public MockDataDescriptorBuilder AddGlobalIndirect(string name, uint indirectIndex, string? typeName = null)
    {
        _globals[name] = new MockDataDescriptorGlobal(name, IndirectIndex: indirectIndex, TypeName: typeName);
        return this;
    }

    public uint AddIndirectValue(ulong value)
    {
        _indirectValues.Add(value);
        return checked((uint)(_indirectValues.Count - 1));
    }

    public MockDataDescriptorBuilder AddSubDescriptor(string name, uint indirectIndex)
    {
        _subDescriptors[name] = new MockSubDataDescriptor(name, indirectIndex);
        return this;
    }

    internal MockDataDescriptor Build()
    {
        return new MockDataDescriptor(
            Contracts: _contracts,
            Types: new Dictionary<string, MockDataDescriptorType>(_types, StringComparer.Ordinal),
            Globals: [.. _globals.Values],
            SubDescriptors: [.. _subDescriptors.Values],
            IndirectValues: _indirectValues);
    }
}



public sealed class MockDataDescriptorType
{
    public MockDataDescriptorType()
    {
    }

    public MockDataDescriptorType(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        Size = checked((uint)layout.Size);

        MockDataDescriptorField[] fields = new MockDataDescriptorField[layout.Fields.Length];
        for (int i = 0; i < layout.Fields.Length; i++)
        {
            LayoutField field = layout.Fields[i];
            fields[i] = new MockDataDescriptorField(field.Name, field.Offset, field.Type?.Name);
        }

        Fields = fields;
    }

    public uint? Size { get; init; }
    public IReadOnlyList<MockDataDescriptorField> Fields { get; init; } = [];

    internal int GetFieldOffset(string fieldName)
    {
        foreach (MockDataDescriptorField field in Fields)
        {
            if (field.Name == fieldName)
            {
                return field.Offset;
            }
        }

        throw new InvalidOperationException($"Field '{fieldName}' not found.");
    }
}

public readonly record struct MockDataDescriptorField(string Name, int Offset, string? TypeName = null);

internal readonly record struct MockDataDescriptorGlobal(
    string Name,
    ulong? Value = null,
    uint? IndirectIndex = null,
    string? StringValue = null,
    string? TypeName = null);

internal readonly record struct MockSubDataDescriptor(string Name, uint IndirectIndex);

internal readonly record struct MockDataDescriptor(
    IReadOnlyDictionary<string, int> Contracts,
    IReadOnlyDictionary<string, MockDataDescriptorType> Types,
    IReadOnlyList<MockDataDescriptorGlobal> Globals,
    IReadOnlyList<MockSubDataDescriptor> SubDescriptors,
    IReadOnlyList<ulong> IndirectValues);

internal static class MockDataDescriptorSerializer
{
    private static readonly ulong s_magic = BitConverter.ToUInt64("DNCCDAC\0"u8);

    internal static int GetDescriptorHeaderSize(bool is64Bit) => is64Bit ? 40 : 32;

    public static ulong CreateDescriptor(
        MockTarget.Architecture architecture,
        MockMemorySpace.BumpAllocator allocator,
        MockDataDescriptor descriptorData,
        string descriptorName)
    {
        string jsonText = BuildJson(descriptorData);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonText);
        byte[] pointerDataBytes = BuildPointerData(descriptorData.IndirectValues, architecture);
        byte[] pointerDataContent = pointerDataBytes.Length == 0 ? [0] : pointerDataBytes;
        byte[] descriptorBytes = new byte[GetDescriptorHeaderSize(architecture.Is64Bit)];

        MockMemorySpace.HeapFragment jsonFragment = allocator.AllocateFragment(
            jsonBytes,
            $"{descriptorName}_Json");
        MockMemorySpace.HeapFragment pointerDataFragment = allocator.AllocateFragment(
            pointerDataContent,
            $"{descriptorName}_PointerData");

        FillHeader(
            descriptorBytes,
            architecture,
            jsonBytes.Length,
            jsonFragment.Address,
            pointerDataBytes.Length / architecture.PointerSize,
            pointerDataFragment.Address);
        MockMemorySpace.HeapFragment descriptorFragment = allocator.AllocateFragment(descriptorBytes, descriptorName);

        return descriptorFragment.Address;
    }

    private static byte[] BuildPointerData(IReadOnlyList<ulong> indirectValues, MockTarget.Architecture architecture)
    {
        if (indirectValues.Count == 0)
        {
            return [];
        }

        byte[] pointerDataBytes = new byte[indirectValues.Count * architecture.PointerSize];
        SpanWriter spanWriter = new(architecture, pointerDataBytes);
        foreach (ulong value in indirectValues)
        {
            spanWriter.WritePointer(value);
        }

        return pointerDataBytes;
    }

    private static string BuildJson(MockDataDescriptor descriptorData)
    {
        StringBuilder builder = new();
        builder.Append('{');
        builder.Append("\"version\":0,");
        builder.Append("\"baseline\":\"empty\",");
        builder.Append("\"contracts\":{");
        AppendContracts(builder, descriptorData.Contracts);
        builder.Append("},");
        builder.Append("\"types\":{");
        AppendTypes(builder, descriptorData.Types);
        builder.Append("},");
        builder.Append("\"globals\":{");
        AppendGlobals(builder, descriptorData.Globals);
        builder.Append("},");
        builder.Append("\"subDescriptors\":{");
        AppendSubDescriptors(builder, descriptorData.SubDescriptors);
        builder.Append('}');
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendContracts(StringBuilder builder, IReadOnlyDictionary<string, int> contracts)
    {
        bool first = true;
        foreach ((string name, int version) in contracts)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            AppendJsonString(builder, name);
            builder.Append(':');
            builder.Append(version);
        }
    }

    private static void AppendTypes(StringBuilder builder, IReadOnlyDictionary<string, MockDataDescriptorType> types)
    {
        bool firstType = true;
        foreach ((string typeName, MockDataDescriptorType descriptor) in types)
        {
            if (!firstType)
            {
                builder.Append(',');
            }

            firstType = false;
            AppendJsonString(builder, typeName);
            builder.Append(":{");

            bool firstField = true;
            if (descriptor.Size is uint size)
            {
                AppendJsonString(builder, "!");
                builder.Append(':');
                builder.Append(size);
                firstField = false;
            }

            foreach (MockDataDescriptorField field in descriptor.Fields)
            {
                if (!firstField)
                {
                    builder.Append(',');
                }

                firstField = false;
                AppendJsonString(builder, field.Name);
                builder.Append(':');

                if (field.TypeName is null)
                {
                    builder.Append(field.Offset);
                }
                else
                {
                    builder.Append('[');
                    builder.Append(field.Offset);
                    builder.Append(',');
                    AppendJsonString(builder, field.TypeName);
                    builder.Append(']');
                }
            }

            builder.Append('}');
        }
    }

    private static void AppendGlobals(StringBuilder builder, IReadOnlyList<MockDataDescriptorGlobal> globals)
    {
        bool first = true;
        foreach (MockDataDescriptorGlobal global in globals)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            AppendJsonString(builder, global.Name);
            builder.Append(':');

            if (global.Value is ulong value)
            {
                AppendValue(builder, value, global.TypeName);
            }
            else if (global.IndirectIndex is uint indirectIndex)
            {
                AppendIndirect(builder, indirectIndex, global.TypeName);
            }
            else if (global.StringValue is string stringValue)
            {
                AppendString(builder, stringValue, global.TypeName);
            }
            else
            {
                throw new InvalidOperationException($"Global '{global.Name}' has no value.");
            }
        }
    }

    private static void AppendSubDescriptors(StringBuilder builder, IReadOnlyList<MockSubDataDescriptor> subDescriptors)
    {
        bool first = true;
        foreach (MockSubDataDescriptor subDescriptor in subDescriptors)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            AppendJsonString(builder, subDescriptor.Name);
            builder.Append(':');
            builder.Append('[');
            builder.Append(subDescriptor.IndirectIndex);
            builder.Append(']');
        }
    }

    private static void AppendValue(StringBuilder builder, ulong value, string? typeName)
    {
        if (typeName is null)
        {
            builder.Append(value);
            return;
        }

        builder.Append('[');
        builder.Append(value);
        builder.Append(',');
        AppendJsonString(builder, typeName);
        builder.Append(']');
    }

    private static void AppendIndirect(StringBuilder builder, uint indirectIndex, string? typeName)
    {
        if (typeName is null)
        {
            builder.Append('[');
            builder.Append(indirectIndex);
            builder.Append(']');
            return;
        }

        builder.Append("[[");
        builder.Append(indirectIndex);
        builder.Append("],");
        AppendJsonString(builder, typeName);
        builder.Append(']');
    }

    private static void AppendString(StringBuilder builder, string value, string? typeName)
    {
        if (typeName is null)
        {
            AppendJsonString(builder, value);
            return;
        }

        builder.Append('[');
        AppendJsonString(builder, value);
        builder.Append(',');
        AppendJsonString(builder, typeName);
        builder.Append(']');
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
    }

    private static void FillHeader(
        Span<byte> destination,
        MockTarget.Architecture architecture,
        int jsonDescriptorSize,
        ulong jsonDescriptorAddress,
        int pointerDataCount,
        ulong pointerDataAddress)
    {
        SpanWriter writer = new(architecture, destination);
        writer.Write(s_magic);
        writer.Write(architecture.Is64Bit ? 0x1u : 0x3u);
        writer.Write((uint)jsonDescriptorSize);

        if (architecture.Is64Bit)
        {
            writer.Write(jsonDescriptorAddress);
            writer.Write((uint)pointerDataCount);
            writer.Write(0u);
            writer.Write(pointerDataAddress);
        }
        else
        {
            writer.Write((uint)jsonDescriptorAddress);
            writer.Write((uint)pointerDataCount);
            writer.Write(0u);
            writer.Write((uint)pointerDataAddress);
        }
    }
}
