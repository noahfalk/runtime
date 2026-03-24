// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public sealed class MockComMethodTable : TypedView
{
    private const string FlagsFieldName = "Flags";
    private const string MethodTableFieldName = "MethodTable";

    public static Layout<MockComMethodTable> CreateLayout(MockTarget.Architecture architecture)
    {
        LayoutBuilder builder = new("ComMethodTable", architecture)
        {
            Size = checked(2 * architecture.PointerSize),
        };

        builder.AddField("Flags", 0, architecture.PointerSize);
        builder.AddField("MethodTable", architecture.PointerSize, architecture.PointerSize);
        return builder.Build<MockComMethodTable>();
    }

    public ulong Flags
    {
        get => ReadPointerField(FlagsFieldName);
        set => WritePointerField(FlagsFieldName, value);
    }

    public ulong MethodTable
    {
        get => ReadPointerField(MethodTableFieldName);
        set => WritePointerField(MethodTableFieldName, value);
    }

    public ulong VTable
        => Address + (ulong)Layout.Size;

    public ulong GetVTableSlot(int index)
        => ReadPointer(GetVTableSlotSpan(index));

    public void SetVTableSlot(int index, ulong value)
        => WritePointer(GetVTableSlotSpan(index), value);

    private Span<byte> GetVTableSlotSpan(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        int trailingOffset = Layout.Size + (index * Architecture.PointerSize);
        int trailingSize = Memory.Length - Layout.Size;
        int slotCount = trailingSize / Architecture.PointerSize;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, slotCount);

        return Memory.Span.Slice(trailingOffset, Architecture.PointerSize);
    }
}

internal sealed class MockInterfaceEntry : TypedView
{
    public static Layout<MockInterfaceEntry> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("InterfaceEntry", architecture)
            .AddPointerField("MethodTable")
            .AddPointerField("Unknown")
            .Build<MockInterfaceEntry>();

    public ulong MethodTable
    {
        get => ReadPointerField("MethodTable");
        set => WritePointerField("MethodTable", value);
    }

    public ulong Unknown
    {
        get => ReadPointerField("Unknown");
        set => WritePointerField("Unknown", value);
    }
}

internal sealed class MockCtxEntry : TypedView
{
    public static Layout<MockCtxEntry> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("CtxEntry", architecture)
            .AddPointerField("STAThread")
            .AddPointerField("CtxCookie")
            .Build<MockCtxEntry>();

    public ulong STAThread
    {
        get => ReadPointerField("STAThread");
        set => WritePointerField("STAThread", value);
    }

    public ulong CtxCookie
    {
        get => ReadPointerField("CtxCookie");
        set => WritePointerField("CtxCookie", value);
    }
}

internal sealed class MockRCW : TypedView
{
    public static Layout<MockRCW> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("RCW", architecture)
            .AddPointerField("NextCleanupBucket")
            .AddPointerField("NextRCW")
            .AddUInt32Field("Flags")
            .AddPointerField("CtxCookie")
            .AddPointerField("CtxEntry")
            .AddPointerField("InterfaceEntries")
            .AddPointerField("IdentityPointer")
            .AddUInt32Field("SyncBlockIndex")
            .AddPointerField("VTablePtr")
            .AddPointerField("CreatorThread")
            .AddUInt32Field("RefCount")
            .AddPointerField("UnknownPointer")
            .Build<MockRCW>();

    public ulong IdentityPointer
    {
        get => ReadPointerField("IdentityPointer");
        set => WritePointerField("IdentityPointer", value);
    }

    public ulong UnknownPointer
    {
        get => ReadPointerField("UnknownPointer");
        set => WritePointerField("UnknownPointer", value);
    }

    public ulong VTablePtr
    {
        get => ReadPointerField("VTablePtr");
        set => WritePointerField("VTablePtr", value);
    }

    public ulong CreatorThread
    {
        get => ReadPointerField("CreatorThread");
        set => WritePointerField("CreatorThread", value);
    }

    public ulong CtxCookie
    {
        get => ReadPointerField("CtxCookie");
        set => WritePointerField("CtxCookie", value);
    }

    public ulong CtxEntry
    {
        get => ReadPointerField("CtxEntry");
        set => WritePointerField("CtxEntry", value);
    }

    public uint SyncBlockIndex
    {
        get => ReadUInt32Field("SyncBlockIndex");
        set => WriteUInt32Field("SyncBlockIndex", value);
    }

    public uint RefCount
    {
        get => ReadUInt32Field("RefCount");
        set => WriteUInt32Field("RefCount", value);
    }

    public uint Flags
    {
        get => ReadUInt32Field("Flags");
        set => WriteUInt32Field("Flags", value);
    }
}

public sealed class MockSimpleComCallWrapper : TypedView
{
    private const int RefCountFieldSize = sizeof(ulong);

    public MockSimpleComCallWrapper()
    {
        VTablePointers = new VTablePointerCollection(this);
    }

    public static Layout<MockSimpleComCallWrapper> CreateLayout(MockTarget.Architecture architecture)
    {
        LayoutBuilder builder = new("SimpleComCallWrapper", architecture)
        {
            Size = checked(12 + (3 * architecture.PointerSize)),
        };

        builder.AddField("RefCount", 0, RefCountFieldSize);
        builder.AddField("Flags", 8, sizeof(uint));
        builder.AddField("MainWrapper", 12, architecture.PointerSize);
        builder.AddField("VTablePtr", 12 + architecture.PointerSize, architecture.PointerSize);
        builder.AddField("OuterIUnknown", 12 + (2 * architecture.PointerSize), architecture.PointerSize);
        return builder.Build<MockSimpleComCallWrapper>();
    }

    public ulong VTablePointerAddress => GetFieldAddress("VTablePtr");

    public VTablePointerCollection VTablePointers { get; }

    public ulong RefCount
    {
        get => ReadUInt64Field("RefCount");
        set => WriteUInt64Field("RefCount", value);
    }

    public uint Flags
    {
        get => ReadUInt32Field("Flags");
        set => WriteUInt32Field("Flags", value);
    }

    public ulong MainWrapper
    {
        get => ReadPointerField("MainWrapper");
        set => WritePointerField("MainWrapper", value);
    }

    public ulong VTablePtr
    {
        get => ReadPointerField("VTablePtr");
        set => WritePointerField("VTablePtr", value);
    }

    public ulong OuterIUnknown
    {
        get => ReadPointerField("OuterIUnknown");
        set => WritePointerField("OuterIUnknown", value);
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
            set => _wrapper.WritePointer(_wrapper.GetVTablePointerSpan(index), value);
        }
    }

    private Span<byte> GetVTablePointerSpan(int index)
        => Memory.Span.Slice(Layout.GetField("MainWrapper").Offset + ((index + 1) * Architecture.PointerSize), Architecture.PointerSize);
}

public sealed class MockComCallWrapper : TypedView
{
    internal static ulong GetRequiredAlignment(MockTarget.Architecture architecture)
        => architecture.Is64Bit ? 64UL : 32UL;

    public MockComCallWrapper()
    {
        InterfacePointers = new InterfacePointerCollection(this);
    }

    public static Layout<MockComCallWrapper> CreateLayout(MockTarget.Architecture architecture)
    {
        LayoutBuilder builder = new("ComCallWrapper", architecture)
        {
            Size = checked(8 * architecture.PointerSize),
        };

        builder.SetAlignment(GetRequiredAlignment(architecture));
        builder.AddField("SimpleWrapper", 0, architecture.PointerSize);
        builder.AddField("IPtr", architecture.PointerSize, architecture.PointerSize);
        builder.AddField("Next", 6 * architecture.PointerSize, architecture.PointerSize);
        builder.AddField("Handle", 7 * architecture.PointerSize, architecture.PointerSize);
        return builder.Build<MockComCallWrapper>();
    }

    public ulong InterfacePointerAddress => GetFieldAddress("IPtr");

    public InterfacePointerCollection InterfacePointers { get; }

    public ulong SimpleWrapper
    {
        get => ReadPointerField("SimpleWrapper");
        set => WritePointerField("SimpleWrapper", value);
    }

    public ulong Next
    {
        get => ReadPointerField("Next");
        set => WritePointerField("Next", value);
    }

    public ulong Handle
    {
        get => ReadPointerField("Handle");
        set => WritePointerField("Handle", value);
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
            set => _wrapper.WritePointer(_wrapper.GetInterfacePointerSpan(index), value);
        }
    }

    private Span<byte> GetInterfacePointerSpan(int index)
        => Memory.Span.Slice(Layout.GetField("IPtr").Offset + (index * Architecture.PointerSize), Architecture.PointerSize);
}

public sealed class MockBuiltInComBuilder
{
    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly MockTarget.Architecture _architecture;
    private readonly Layout _codePointerLayout;

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
        _architecture = architecture;

        LayoutBuilder codePointerLayoutBuilder = new("CodePointer", architecture)
        {
            Size = architecture.PointerSize,
        };
        _codePointerLayout = codePointerLayoutBuilder.Build();

        ComCallWrapperLayout = MockComCallWrapper.CreateLayout(architecture);
        SimpleComCallWrapperLayout = MockSimpleComCallWrapper.CreateLayout(architecture);
        ComMethodTableLayout = MockComMethodTable.CreateLayout(architecture);
        InterfaceEntryLayout = MockInterfaceEntry.CreateLayout(architecture);
        CtxEntryLayout = MockCtxEntry.CreateLayout(architecture);
        RCWLayout = MockRCW.CreateLayout(architecture);

        TearOffAddRefAddress = AllocateCodeAddress(allocator, "TearOffAddRefCode");
        TearOffAddRefSimpleAddress = AllocateCodeAddress(allocator, "TearOffAddRefSimpleCode");
        TearOffAddRefSimpleInnerAddress = AllocateCodeAddress(allocator, "TearOffAddRefSimpleInnerCode");

        TearOffAddRefSlot = allocator.AllocatePointer(TearOffAddRefAddress, "TearOffAddRefSlot");
        TearOffAddRefSimpleSlot = allocator.AllocatePointer(TearOffAddRefSimpleAddress, "TearOffAddRefSimpleSlot");
        TearOffAddRefSimpleInnerSlot = allocator.AllocatePointer(TearOffAddRefSimpleInnerAddress, "TearOffAddRefSimpleInnerSlot");
    }

    internal Layout CodePointerLayout => _codePointerLayout;

    internal Layout<MockComCallWrapper> ComCallWrapperLayout { get; }

    internal Layout<MockSimpleComCallWrapper> SimpleComCallWrapperLayout { get; }

    internal Layout<MockComMethodTable> ComMethodTableLayout { get; }

    internal Layout<MockInterfaceEntry> InterfaceEntryLayout { get; }

    internal Layout<MockCtxEntry> CtxEntryLayout { get; }

    internal Layout<MockRCW> RCWLayout { get; }

    public MockSimpleComCallWrapper AddSimpleComCallWrapper()
        => SimpleComCallWrapperLayout.Allocate(_allocator, "SimpleComCallWrapper");

    public MockComCallWrapper AddComCallWrapper()
        => ComCallWrapperLayout.Allocate(_allocator, "ComCallWrapper");

    public MockComMethodTable AddComMethodTable(int vtableSlots = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vtableSlots);

        int totalSize = checked(ComMethodTableLayout.Size + (vtableSlots * _architecture.PointerSize));
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment((ulong)totalSize, "ComMethodTable");
        return ComMethodTableLayout.Create(fragment.Data.AsMemory(0, totalSize), fragment.Address);
    }

    public ulong AddRCWWithInlineEntries((ulong MethodTable, ulong Unknown)[] entries, ulong ctxCookie = 0)
    {
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment((ulong)GetFullRcwSize(), "RCW with inline entries");
        MockRCW rcw = RCWLayout.Create(fragment);
        rcw.CtxCookie = ctxCookie;

        int interfaceCacheSize = checked((int)RCWInterfaceCacheSize);
        int entryCount = Math.Min(entries.Length, interfaceCacheSize);
        for (int i = 0; i < entryCount; i++)
        {
            MockInterfaceEntry entry = CreateRcwInterfaceEntry(rcw, i);
            entry.MethodTable = entries[i].MethodTable;
            entry.Unknown = entries[i].Unknown;
        }

        return rcw.Address;
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
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment((ulong)GetFullRcwSize(), "Full RCW");
        MockRCW rcw = RCWLayout.Create(fragment);
        rcw.IdentityPointer = identityPointer;
        rcw.UnknownPointer = unknownPointer;
        rcw.VTablePtr = vtablePtr;
        rcw.CreatorThread = creatorThread;
        rcw.CtxCookie = ctxCookie;
        rcw.CtxEntry = ctxEntry;
        rcw.SyncBlockIndex = syncBlockIndex;
        rcw.RefCount = refCount;
        rcw.Flags = flags;
        return rcw.Address;
    }

    public ulong AddCtxEntry(ulong staThread = 0, ulong ctxCookie = 0)
    {
        MockCtxEntry entry = CtxEntryLayout.Allocate(_allocator, "CtxEntry");
        entry.STAThread = staThread;
        entry.CtxCookie = ctxCookie;
        return entry.Address;
    }

    public MockMemorySpace.HeapFragment AllocateFragment(ulong size, string? name = null, ulong? alignment = null)
        => _allocator.AllocateFragment(size, name, alignment);

    public void AdvanceTo(ulong address)
        => _allocator.AdvanceTo(address);

    internal static ulong GetCCWThisMask(MockTarget.Architecture architecture)
        => architecture.Is64Bit ? ~0x3FUL : ~0x1FUL;

    private MockInterfaceEntry CreateRcwInterfaceEntry(MockRCW rcw, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, checked((int)RCWInterfaceCacheSize));

        int offset = RCWLayout.GetField("InterfaceEntries").Offset + (index * InterfaceEntryLayout.Size);
        return InterfaceEntryLayout.Create(
            rcw.Memory.Slice(offset, InterfaceEntryLayout.Size),
            rcw.Address + (ulong)offset);
    }

    private int GetFullRcwSize()
        => checked(RCWLayout.GetField("InterfaceEntries").Offset + (InterfaceEntryLayout.Size * (int)RCWInterfaceCacheSize));

    private static ulong AllocateCodeAddress(MockMemorySpace.BumpAllocator allocator, string name)
        => allocator.AllocateFragment(1, name).Address;
}

public static class MockBuiltInComBuilderExtensions
{
    private const string BuiltInCOMContractName = "BuiltInCOM";

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
                descriptor.AddType(config.CodePointerLayout);
                descriptor.AddType(config.ComCallWrapperLayout);
                descriptor.AddType(config.SimpleComCallWrapperLayout);
                descriptor.AddType(config.ComMethodTableLayout);
                descriptor.AddType(config.InterfaceEntryLayout);
                descriptor.AddType(config.CtxEntryLayout);
                descriptor.AddType(config.RCWLayout);
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
}
