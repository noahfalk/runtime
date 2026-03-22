// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public sealed class MockSyncBlockBuilder
{
    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly MockTarget.Architecture _architecture;
    private readonly MockMemorySpace.HeapFragment _syncBlockCacheFragment;

    private ulong _cleanupListHead;

    internal MockSyncBlockBuilder(MockMemorySpace.BumpAllocator allocator, MockTarget.Architecture architecture)
    {
        _allocator = allocator;
        _architecture = architecture;

        _syncBlockCacheFragment = _allocator.AllocateFragment((ulong)GetSyncBlockCacheSize(), "SyncBlockCache");
        SyncBlockCacheGlobalAddress = _allocator.AllocatePointer(_syncBlockCacheFragment.Address, "[global pointer] SyncBlockCache");

        ulong syncTableEntriesAddress = _allocator.AllocateFragment((ulong)(2 * _architecture.PointerSize), "SyncTableEntries").Address;
        SyncTableEntriesGlobalAddress = _allocator.AllocatePointer(syncTableEntriesAddress, "[global pointer] SyncTableEntries");

        SpanWriter writer = new(_architecture, _syncBlockCacheFragment.Data);
        writer.Write((uint)1);
        UpdateCleanupBlockList(0);
    }

    public ulong SyncBlockCacheGlobalAddress { get; }
    public ulong SyncTableEntriesGlobalAddress { get; }

    public ulong AddSyncBlockToCleanupList(ulong rcw, ulong ccw, ulong ccf, bool hasInteropInfo = true)
    {
        int syncBlockSize = GetSyncBlockSize();
        int interopInfoSize = hasInteropInfo ? 3 * _architecture.PointerSize : 0;
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment(
            checked((ulong)(syncBlockSize + interopInfoSize)),
            "SyncBlock (cleanup)");

        Span<byte> syncBlockData = fragment.Data.AsSpan(0, syncBlockSize);
        if (hasInteropInfo)
        {
            ulong interopInfoAddress = fragment.Address + (ulong)syncBlockSize;
            Span<byte> interopInfoData = fragment.Data.AsSpan(syncBlockSize, interopInfoSize);
            SpanWriter writer = new(_architecture, interopInfoData);
            writer.WritePointer(rcw);
            writer.WritePointer(ccw);
            writer.WritePointer(ccf);

            WritePointer(syncBlockData.Slice(InteropInfoOffset, _architecture.PointerSize), interopInfoAddress);
        }

        WritePointer(syncBlockData.Slice(GetSyncBlockLinkNextOffset(), _architecture.PointerSize), _cleanupListHead);

        ulong syncBlockAddress = fragment.Address;
        _cleanupListHead = syncBlockAddress + (ulong)GetSyncBlockLinkNextOffset();
        UpdateCleanupBlockList(_cleanupListHead);

        return syncBlockAddress;
    }

    private void UpdateCleanupBlockList(ulong cleanupListHead)
        => WritePointer(
            _syncBlockCacheFragment.Data.AsSpan(GetCleanupBlockListOffset(), _architecture.PointerSize),
            cleanupListHead);

    private void WritePointer(Span<byte> destination, ulong value)
    {
        SpanWriter writer = new(_architecture, destination);
        writer.WritePointer(value);
    }

    private int GetSyncBlockCacheSize() => GetCleanupBlockListOffset() + _architecture.PointerSize;

    private int GetCleanupBlockListOffset()
        => _architecture.Is64Bit ? sizeof(ulong) : sizeof(uint);

    private int GetSyncBlockSize() => GetSyncBlockLinkNextOffset() + _architecture.PointerSize;

    private int GetSyncBlockLinkNextOffset()
        => _architecture.Is64Bit ? 3 * sizeof(ulong) : 3 * sizeof(uint);

    private const int InteropInfoOffset = 0;
}

public static class MockSyncBlockBuilderExtensions
{
    private const string SyncBlockContractName = "SyncBlock";
    private const string SyncBlockCacheTypeName = "SyncBlockCache";
    private const string SyncTableEntryTypeName = "SyncTableEntry";
    private const string SyncBlockTypeName = "SyncBlock";
    private const string InteropSyncBlockInfoTypeName = "InteropSyncBlockInfo";

    public static MockProcessBuilder AddSyncBlock(
        this MockProcessBuilder processBuilder,
        Action<MockSyncBlockBuilder> configure)
    {
        MockMemorySpace.BumpAllocator allocator = processBuilder.MemoryBuilder.DefaultAllocator;
        MockSyncBlockBuilder config = new(allocator, processBuilder.Architecture);
        configure(config);

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptor =>
            {
                AddTypes(descriptor);
                descriptor
                    .AddContract(SyncBlockContractName, 1)
                    .AddGlobalValue("SyncBlockCache", config.SyncBlockCacheGlobalAddress)
                    .AddGlobalValue("SyncTableEntries", config.SyncTableEntriesGlobalAddress);
            });
        });

        return processBuilder;
    }

    private static void AddTypes(MockDataDescriptorBuilder descriptor)
    {
        descriptor.AddSequentialType(SyncBlockCacheTypeName, type =>
        {
            type.AddUInt32Field("FreeSyncTableIndex");
            type.AddPointerField("CleanupBlockList");
        });

        descriptor.AddSequentialType(SyncTableEntryTypeName, type =>
        {
            type.AddPointerField("SyncBlock");
            type.AddPointerField("Object");
        });

        descriptor.AddSequentialType(SyncBlockTypeName, type =>
        {
            type.AddPointerField("InteropInfo");
            type.AddPointerField("Lock");
            type.AddUInt32Field("ThinLock");
            type.AddPointerField("LinkNext");
        });

        descriptor.AddSequentialType(InteropSyncBlockInfoTypeName, type =>
        {
            type.AddPointerField("RCW");
            type.AddPointerField("CCW");
            type.AddPointerField("CCF");
        });
    }
}
