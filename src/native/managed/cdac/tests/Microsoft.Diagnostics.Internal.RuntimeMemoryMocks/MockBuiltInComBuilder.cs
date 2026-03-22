// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public sealed class MockBuiltInComBuilder
{
    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly MockMemoryHelpers _memoryHelpers;

    public const uint DefaultNumVtablePtrs = 5;
    public const uint DefaultRCWInterfaceCacheSize = 8;

    public uint CCWNumInterfaces { get; set; } = DefaultNumVtablePtrs;
    public uint RCWInterfaceCacheSize { get; set; } = DefaultRCWInterfaceCacheSize;
    public ulong TearOffAddRefSlot { get; }
    public ulong TearOffAddRefAddress { get; }
    public ulong TearOffAddRefSimpleSlot { get; }
    public ulong TearOffAddRefSimpleAddress { get; }
    public ulong TearOffAddRefSimpleInnerSlot { get; }
    public ulong TearOffAddRefSimpleInnerAddress { get; }

    internal MockBuiltInComBuilder(MockMemorySpace.BumpAllocator allocator, MockTarget.Architecture architecture)
    {
        _allocator = allocator;
        _memoryHelpers = new(architecture);

        TearOffAddRefAddress = AllocateCodeAddress(allocator, "TearOffAddRefCode");
        TearOffAddRefSimpleAddress = AllocateCodeAddress(allocator, "TearOffAddRefSimpleCode");
        TearOffAddRefSimpleInnerAddress = AllocateCodeAddress(allocator, "TearOffAddRefSimpleInnerCode");

        TearOffAddRefSlot = allocator.AllocatePointer(TearOffAddRefAddress, "TearOffAddRefSlot");
        TearOffAddRefSimpleSlot = allocator.AllocatePointer(TearOffAddRefSimpleAddress, "TearOffAddRefSimpleSlot");
        TearOffAddRefSimpleInnerSlot = allocator.AllocatePointer(TearOffAddRefSimpleInnerAddress, "TearOffAddRefSimpleInnerSlot");
    }

    public MockSimpleComCallWrapper AddSimpleComCallWrapper()
    {
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment(
            checked((ulong)(12 + (3 * _memoryHelpers.PointerSize))),
            "SimpleComCallWrapper");
        return new MockSimpleComCallWrapper(_memoryHelpers, fragment);
    }

    public MockComCallWrapper AddComCallWrapper(ulong? alignment = null)
    {
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment(
            checked((ulong)(8 * _memoryHelpers.PointerSize)),
            "ComCallWrapper",
            alignment);
        return new MockComCallWrapper(_memoryHelpers, fragment);
    }

    public ulong AddRCWWithInlineEntries((ulong MethodTable, ulong Unknown)[] entries, ulong ctxCookie = 0)
    {
        int entrySize = GetInterfaceEntrySize();
        int entriesOffset = GetRcwInterfaceEntriesOffset();
        int interfaceCacheSize = checked((int)RCWInterfaceCacheSize);
        int totalSize = checked(entriesOffset + (entrySize * interfaceCacheSize));

        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment((ulong)totalSize, "RCW with inline entries");
        Span<byte> data = fragment.Data;

        WritePointer(data.Slice(GetRcwCtxCookieOffset(), _memoryHelpers.PointerSize), ctxCookie);

        for (int i = 0; i < entries.Length && i < interfaceCacheSize; i++)
        {
            Span<byte> entryData = data.Slice(entriesOffset + (i * entrySize), entrySize);
            WritePointer(entryData.Slice(0, _memoryHelpers.PointerSize), entries[i].MethodTable);
            WritePointer(entryData.Slice(_memoryHelpers.PointerSize, _memoryHelpers.PointerSize), entries[i].Unknown);
        }

        return fragment.Address;
    }

    public ulong AddFullRCW(
        ulong identityPointer = 0,
        ulong unknownPointer = 0,
        ulong vtablePtr = 0,
        ulong creatorThread = 0,
        ulong ctxCookie = 0,
        ulong ctxEntry = 0,
        uint syncBlockIndex = 0,
        uint refCount = 0,
        uint flags = 0)
    {
        int totalSize = checked(GetRcwInterfaceEntriesOffset() + (GetInterfaceEntrySize() * checked((int)RCWInterfaceCacheSize)));
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment((ulong)totalSize, "Full RCW");
        Span<byte> data = fragment.Data;

        WritePointer(data.Slice(GetRcwIdentityPointerOffset(), _memoryHelpers.PointerSize), identityPointer);
        WritePointer(data.Slice(GetRcwUnknownPointerOffset(), _memoryHelpers.PointerSize), unknownPointer);
        WritePointer(data.Slice(GetRcwVTablePtrOffset(), _memoryHelpers.PointerSize), vtablePtr);
        WritePointer(data.Slice(GetRcwCreatorThreadOffset(), _memoryHelpers.PointerSize), creatorThread);
        WritePointer(data.Slice(GetRcwCtxCookieOffset(), _memoryHelpers.PointerSize), ctxCookie);
        WritePointer(data.Slice(GetRcwCtxEntryOffset(), _memoryHelpers.PointerSize), ctxEntry);
        _memoryHelpers.Write(data.Slice(GetRcwSyncBlockIndexOffset(), sizeof(uint)), syncBlockIndex);
        _memoryHelpers.Write(data.Slice(GetRcwRefCountOffset(), sizeof(uint)), refCount);
        _memoryHelpers.Write(data.Slice(GetRcwFlagsOffset(), sizeof(uint)), flags);

        return fragment.Address;
    }

    public ulong AddCtxEntry(ulong staThread = 0, ulong ctxCookie = 0)
    {
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment((ulong)GetCtxEntrySize(), "CtxEntry");
        Span<byte> data = fragment.Data;

        WritePointer(data.Slice(0, _memoryHelpers.PointerSize), staThread);
        WritePointer(data.Slice(_memoryHelpers.PointerSize, _memoryHelpers.PointerSize), ctxCookie);

        return fragment.Address;
    }

    public MockMemorySpace.HeapFragment AllocateFragment(ulong size, string? name = null, ulong? alignment = null)
        => _allocator.AllocateFragment(size, name, alignment);

    public void AdvanceTo(ulong address)
        => _allocator.AdvanceTo(address);

    internal static ulong GetCCWThisMask(MockTarget.Architecture architecture)
        => architecture.Is64Bit ? ~0x3FUL : ~0x1FUL;

    private void WritePointer(Span<byte> destination, ulong value)
        => _memoryHelpers.WritePointer(destination, value);

    private int GetInterfaceEntrySize() => 2 * _memoryHelpers.PointerSize;

    private int GetCtxEntrySize() => 2 * _memoryHelpers.PointerSize;

    private int GetRcwFlagsOffset() => 2 * _memoryHelpers.PointerSize;

    private int GetRcwCtxCookieOffset() => Align(GetRcwFlagsOffset() + sizeof(uint), _memoryHelpers.PointerSize);

    private int GetRcwCtxEntryOffset() => GetRcwCtxCookieOffset() + _memoryHelpers.PointerSize;

    private int GetRcwInterfaceEntriesOffset() => GetRcwCtxEntryOffset() + _memoryHelpers.PointerSize;

    private int GetRcwIdentityPointerOffset() => GetRcwInterfaceEntriesOffset() + _memoryHelpers.PointerSize * 1;

    private int GetRcwSyncBlockIndexOffset() => GetRcwIdentityPointerOffset() + _memoryHelpers.PointerSize;

    private int GetRcwVTablePtrOffset() => Align(GetRcwSyncBlockIndexOffset() + sizeof(uint), _memoryHelpers.PointerSize);

    private int GetRcwCreatorThreadOffset() => GetRcwVTablePtrOffset() + _memoryHelpers.PointerSize;

    private int GetRcwRefCountOffset() => GetRcwCreatorThreadOffset() + _memoryHelpers.PointerSize;

    private int GetRcwUnknownPointerOffset() => Align(GetRcwRefCountOffset() + sizeof(uint), _memoryHelpers.PointerSize);

    private static int Align(int offset, int alignment)
        => ((offset + alignment) - 1) & ~(alignment - 1);

    private static ulong AllocateCodeAddress(MockMemorySpace.BumpAllocator allocator, string name)
        => allocator.AllocateFragment(1, name).Address;
}

public sealed class MockSimpleComCallWrapper
{
    private const int RefCountOffset = 0;
    private const int FlagsOffset = 8;
    private const int MainWrapperOffset = 12;

    private readonly MockMemoryHelpers _memoryHelpers;
    private readonly MockMemorySpace.HeapFragment _fragment;

    internal MockSimpleComCallWrapper(MockMemoryHelpers memoryHelpers, MockMemorySpace.HeapFragment fragment)
    {
        _memoryHelpers = memoryHelpers;
        _fragment = fragment;
        VTablePointers = new VTablePointerCollection(this);
    }

    public ulong Address => _fragment.Address;
    public ulong VTablePointerAddress => _fragment.Address + (ulong)(MainWrapperOffset + _memoryHelpers.PointerSize);
    public VTablePointerCollection VTablePointers { get; }

    public ulong RefCount
    {
        get => ReadUInt64(_fragment.Data.AsSpan(RefCountOffset, sizeof(ulong)));
        set => _memoryHelpers.Write(_fragment.Data.AsSpan(RefCountOffset, sizeof(ulong)), value);
    }

    public uint Flags
    {
        get => ReadUInt32(_fragment.Data.AsSpan(FlagsOffset, sizeof(uint)));
        set => _memoryHelpers.Write(_fragment.Data.AsSpan(FlagsOffset, sizeof(uint)), value);
    }

    public ulong MainWrapper
    {
        get => ReadPointer(_fragment.Data.AsSpan(MainWrapperOffset, _memoryHelpers.PointerSize));
        set => _memoryHelpers.WritePointer(_fragment.Data.AsSpan(MainWrapperOffset, _memoryHelpers.PointerSize), value);
    }

    public ulong VTablePtr
    {
        get => ReadPointer(_fragment.Data.AsSpan(MainWrapperOffset + _memoryHelpers.PointerSize, _memoryHelpers.PointerSize));
        set => _memoryHelpers.WritePointer(_fragment.Data.AsSpan(MainWrapperOffset + _memoryHelpers.PointerSize, _memoryHelpers.PointerSize), value);
    }

    public ulong OuterIUnknown
    {
        get => ReadPointer(_fragment.Data.AsSpan(MainWrapperOffset + (2 * _memoryHelpers.PointerSize), _memoryHelpers.PointerSize));
        set => _memoryHelpers.WritePointer(_fragment.Data.AsSpan(MainWrapperOffset + (2 * _memoryHelpers.PointerSize), _memoryHelpers.PointerSize), value);
    }

    public sealed class VTablePointerCollection
    {
        private readonly MockSimpleComCallWrapper _wrapper;

        internal VTablePointerCollection(MockSimpleComCallWrapper wrapper)
        {
            _wrapper = wrapper;
        }

        public ulong this[int index]
        {
            get => _wrapper.ReadPointer(_wrapper.GetVTablePointerSpan(index));
            set => _wrapper._memoryHelpers.WritePointer(_wrapper.GetVTablePointerSpan(index), value);
        }
    }

    private uint ReadUInt32(ReadOnlySpan<byte> source)
        => _memoryHelpers.Arch.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source)
            : BinaryPrimitives.ReadUInt32BigEndian(source);

    private ulong ReadUInt64(ReadOnlySpan<byte> source)
        => _memoryHelpers.Arch.IsLittleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(source)
            : BinaryPrimitives.ReadUInt64BigEndian(source);

    private ulong ReadPointer(ReadOnlySpan<byte> source)
        => _memoryHelpers.Arch.Is64Bit ? ReadUInt64(source) : ReadUInt32(source);

    private Span<byte> GetVTablePointerSpan(int index)
        => _fragment.Data.AsSpan(MainWrapperOffset + ((index + 1) * _memoryHelpers.PointerSize), _memoryHelpers.PointerSize);
}

public sealed class MockComCallWrapper
{
    private const int SimpleWrapperOffset = 0;
    private const int InterfacePointerOffset = 1;

    private readonly MockMemoryHelpers _memoryHelpers;
    private readonly MockMemorySpace.HeapFragment _fragment;

    internal MockComCallWrapper(MockMemoryHelpers memoryHelpers, MockMemorySpace.HeapFragment fragment)
    {
        _memoryHelpers = memoryHelpers;
        _fragment = fragment;
        InterfacePointers = new InterfacePointerCollection(this);
    }

    public ulong Address => _fragment.Address;
    public ulong InterfacePointerAddress => _fragment.Address + (ulong)(InterfacePointerOffset * _memoryHelpers.PointerSize);
    public InterfacePointerCollection InterfacePointers { get; }

    public ulong SimpleWrapper
    {
        get => ReadPointer(_fragment.Data.AsSpan(SimpleWrapperOffset, _memoryHelpers.PointerSize));
        set => _memoryHelpers.WritePointer(_fragment.Data.AsSpan(SimpleWrapperOffset, _memoryHelpers.PointerSize), value);
    }

    public ulong Next
    {
        get => ReadPointer(_fragment.Data.AsSpan(6 * _memoryHelpers.PointerSize, _memoryHelpers.PointerSize));
        set => _memoryHelpers.WritePointer(_fragment.Data.AsSpan(6 * _memoryHelpers.PointerSize, _memoryHelpers.PointerSize), value);
    }

    public ulong Handle
    {
        get => ReadPointer(_fragment.Data.AsSpan(7 * _memoryHelpers.PointerSize, _memoryHelpers.PointerSize));
        set => _memoryHelpers.WritePointer(_fragment.Data.AsSpan(7 * _memoryHelpers.PointerSize, _memoryHelpers.PointerSize), value);
    }

    public sealed class InterfacePointerCollection
    {
        private readonly MockComCallWrapper _wrapper;

        internal InterfacePointerCollection(MockComCallWrapper wrapper)
        {
            _wrapper = wrapper;
        }

        public ulong this[int index]
        {
            get => _wrapper.ReadPointer(_wrapper.GetInterfacePointerSpan(index));
            set => _wrapper._memoryHelpers.WritePointer(_wrapper.GetInterfacePointerSpan(index), value);
        }
    }

    private uint ReadUInt32(ReadOnlySpan<byte> source)
        => _memoryHelpers.Arch.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source)
            : BinaryPrimitives.ReadUInt32BigEndian(source);

    private ulong ReadUInt64(ReadOnlySpan<byte> source)
        => _memoryHelpers.Arch.IsLittleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(source)
            : BinaryPrimitives.ReadUInt64BigEndian(source);

    private ulong ReadPointer(ReadOnlySpan<byte> source)
        => _memoryHelpers.Arch.Is64Bit ? ReadUInt64(source) : ReadUInt32(source);

    private Span<byte> GetInterfacePointerSpan(int index)
        => _fragment.Data.AsSpan((InterfacePointerOffset + index) * _memoryHelpers.PointerSize, _memoryHelpers.PointerSize);
}

public static class MockBuiltInComBuilderExtensions
{
    private const string BuiltInCOMContractName = "BuiltInCOM";
    private const string CodePointerTypeName = "CodePointer";
    private const string ComCallWrapperTypeName = "ComCallWrapper";
    private const string SimpleComCallWrapperTypeName = "SimpleComCallWrapper";
    private const string ComMethodTableTypeName = "ComMethodTable";
    private const string RCWTypeName = "RCW";
    private const string InterfaceEntryTypeName = "InterfaceEntry";
    private const string CtxEntryTypeName = "CtxEntry";

    public static MockProcessBuilder AddBuiltInCom(
        this MockProcessBuilder processBuilder,
        Action<MockBuiltInComBuilder> configure)
    {
        MockMemorySpace.BumpAllocator allocator = processBuilder.MemoryBuilder.DefaultAllocator;
        MockBuiltInComBuilder config = new(allocator, processBuilder.Architecture);
        configure(config);

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptor =>
            {
                AddTypes(descriptor, processBuilder.Architecture.PointerSize);
                descriptor
                    .AddContract(BuiltInCOMContractName, 1)
                    .AddGlobalValue("CCWNumInterfaces", config.CCWNumInterfaces)
                    .AddGlobalValue("CCWThisMask", MockBuiltInComBuilder.GetCCWThisMask(processBuilder.Architecture))
                    .AddGlobalValue("RCWInterfaceCacheSize", config.RCWInterfaceCacheSize)
                    .AddGlobalValue("TearOffAddRef", config.TearOffAddRefSlot)
                    .AddGlobalValue("TearOffAddRefSimple", config.TearOffAddRefSimpleSlot)
                    .AddGlobalValue("TearOffAddRefSimpleInner", config.TearOffAddRefSimpleInnerSlot);
            });
        });

        return processBuilder;
    }

    private static void AddTypes(MockDataDescriptorBuilder descriptor, int pointerSize)
    {
        descriptor.AddType(CodePointerTypeName, type =>
        {
            type.Size = (uint)pointerSize;
        });

        descriptor.AddType(ComCallWrapperTypeName, type =>
        {
            type.AddField("SimpleWrapper", 0);
            type.AddField("IPtr", pointerSize);
            type.AddField("Next", 6 * pointerSize);
            type.AddField("Handle", 7 * pointerSize);
        });

        descriptor.AddType(SimpleComCallWrapperTypeName, type =>
        {
            type.AddField("RefCount", 0);
            type.AddField("Flags", 8);
            type.AddField("MainWrapper", 12);
            type.AddField("VTablePtr", 12 + pointerSize);
            type.AddField("OuterIUnknown", 12 + (2 * pointerSize));
        });

        descriptor.AddType(ComMethodTableTypeName, type =>
        {
            type.Size = (uint)(2 * pointerSize);
            type.AddField("Flags", 0);
            type.AddField("MethodTable", pointerSize);
        });

        descriptor.AddSequentialType(InterfaceEntryTypeName, type =>
        {
            type.AddPointerField("MethodTable");
            type.AddPointerField("Unknown");
        });

        descriptor.AddSequentialType(CtxEntryTypeName, type =>
        {
            type.AddPointerField("STAThread");
            type.AddPointerField("CtxCookie");
        });

        descriptor.AddSequentialType(RCWTypeName, type =>
        {
            type.AddPointerField("NextCleanupBucket");
            type.AddPointerField("NextRCW");
            type.AddUInt32Field("Flags");
            type.AddPointerField("CtxCookie");
            type.AddPointerField("CtxEntry");
            type.AddPointerField("InterfaceEntries");
            type.AddPointerField("IdentityPointer");
            type.AddUInt32Field("SyncBlockIndex");
            type.AddPointerField("VTablePtr");
            type.AddPointerField("CreatorThread");
            type.AddUInt32Field("RefCount");
            type.AddPointerField("UnknownPointer");
        });
    }
}
