// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public sealed class MockThreadBuilder
{
    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly MockTarget.Architecture _architecture;
    private readonly MockDataDescriptorType _exceptionInfoType;
    private readonly MockDataDescriptorType _threadType;
    private readonly MockDataDescriptorType _threadStoreType;
    private readonly MockDataDescriptorType _runtimeThreadLocalsType;
    private readonly MockDataDescriptorType _eeAllocContextType;
    private readonly MockDataDescriptorType _gcAllocContextType;
    private readonly Dictionary<ulong, ulong> _exceptionInfosByThread = [];

    private ulong _previousThreadAddress;
    private ulong _threadStoreAddress;

    internal MockThreadBuilder(MockMemorySpace.BumpAllocator allocator, MockTarget.Architecture architecture)
    {
        _allocator = allocator;
        _architecture = architecture;

        ThreadDescriptorTypes types = CreateTypes(architecture);
        _exceptionInfoType = types.ExceptionInfo;
        _threadType = types.Thread;
        _threadStoreType = types.ThreadStore;
        _runtimeThreadLocalsType = types.RuntimeThreadLocals;
        _eeAllocContextType = types.EEAllocContext;
        _gcAllocContextType = types.GCAllocContext;

        _threadStoreAddress = _allocator.AllocateFragment((ulong)GetRequiredSize(_threadStoreType), "ThreadStore").Address;
        ThreadStoreGlobalAddress = _allocator.AllocatePointer(_threadStoreAddress, "[global pointer] ThreadStore");

        FinalizerThreadAddress = _allocator.AllocateFragment((ulong)GetRequiredSize(_threadType), "Finalizer thread").Address;
        FinalizerThreadGlobalAddress = _allocator.AllocatePointer(FinalizerThreadAddress, "[global pointer] Finalizer thread");

        GCThreadAddress = _allocator.AllocateFragment((ulong)GetRequiredSize(_threadType), "GC thread").Address;
        GCThreadGlobalAddress = _allocator.AllocatePointer(GCThreadAddress, "[global pointer] GC thread");
    }

    public ulong ThreadStoreGlobalAddress { get; }
    public ulong FinalizerThreadGlobalAddress { get; }
    public ulong FinalizerThreadAddress { get; }
    public ulong GCThreadGlobalAddress { get; }
    public ulong GCThreadAddress { get; }

    public void SetThreadCounts(int threadCount, int unstartedCount, int backgroundCount, int pendingCount, int deadCount)
    {
        Span<byte> data = BorrowFragmentData(_threadStoreAddress, _threadStoreType);
        WriteInt32(data, _threadStoreType.GetFieldOffset("ThreadCount"), threadCount);
        WriteInt32(data, _threadStoreType.GetFieldOffset("UnstartedCount"), unstartedCount);
        WriteInt32(data, _threadStoreType.GetFieldOffset("BackgroundCount"), backgroundCount);
        WriteInt32(data, _threadStoreType.GetFieldOffset("PendingCount"), pendingCount);
        WriteInt32(data, _threadStoreType.GetFieldOffset("DeadCount"), deadCount);
    }

    public ulong AddThread(uint id, ulong osId)
        => AddThread(id, osId, allocBytes: 0, allocBytesLoh: 0);

    public ulong AddThread(uint id, ulong osId, long allocBytes, long allocBytesLoh)
    {
        MockMemorySpace.HeapFragment exceptionInfoFragment = _allocator.AllocateFragment((ulong)GetRequiredSize(_exceptionInfoType), "ExceptionInfo");
        MockMemorySpace.HeapFragment runtimeThreadLocalsFragment = _allocator.AllocateFragment((ulong)GetRequiredSize(_runtimeThreadLocalsType), "RuntimeThreadLocals");
        MockMemorySpace.HeapFragment threadFragment = _allocator.AllocateFragment((ulong)GetRequiredSize(_threadType), "Thread");

        Span<byte> threadData = threadFragment.Data;
        WriteUInt32(threadData, _threadType.GetFieldOffset("Id"), id);
        WritePointer(threadData, _threadType.GetFieldOffset("OSId"), osId);
        WritePointer(threadData, _threadType.GetFieldOffset("ExceptionTracker"), exceptionInfoFragment.Address);
        WritePointer(threadData, _threadType.GetFieldOffset("RuntimeThreadLocals"), runtimeThreadLocalsFragment.Address);

        Span<byte> runtimeThreadLocalsData = runtimeThreadLocalsFragment.Data;
        int allocContextOffset = _runtimeThreadLocalsType.GetFieldOffset("AllocContext");
        int gcAllocationContextOffset = _eeAllocContextType.GetFieldOffset("GCAllocationContext");
        int allocBytesOffset = _gcAllocContextType.GetFieldOffset("AllocBytes");
        int allocBytesLohOffset = _gcAllocContextType.GetFieldOffset("AllocBytesLoh");
        int baseOffset = checked(allocContextOffset + gcAllocationContextOffset);
        WriteInt64(runtimeThreadLocalsData, checked(baseOffset + allocBytesOffset), allocBytes);
        WriteInt64(runtimeThreadLocalsData, checked(baseOffset + allocBytesLohOffset), allocBytesLoh);

        ulong threadLinkAddress = threadFragment.Address + (ulong)_threadType.GetFieldOffset("LinkNext");
        if (_previousThreadAddress == 0)
        {
            WritePointer(
                BorrowAddressRange(_threadStoreAddress + (ulong)_threadStoreType.GetFieldOffset("FirstThreadLink"), _architecture.PointerSize),
                threadLinkAddress);
        }
        else
        {
            WritePointer(
                BorrowAddressRange(_previousThreadAddress + (ulong)_threadType.GetFieldOffset("LinkNext"), _architecture.PointerSize),
                threadLinkAddress);
        }

        _previousThreadAddress = threadFragment.Address;
        _exceptionInfosByThread[threadFragment.Address] = exceptionInfoFragment.Address;
        return threadFragment.Address;
    }

    public void SetStackLimits(ulong threadAddress, ulong stackBase, ulong stackLimit)
    {
        Span<byte> data = BorrowFragmentData(threadAddress, _threadType);
        WritePointer(data, _threadType.GetFieldOffset("CachedStackBase"), stackBase);
        WritePointer(data, _threadType.GetFieldOffset("CachedStackLimit"), stackLimit);
    }

    public void SetExceptionTracker(ulong threadAddress, ulong exceptionInfoAddress)
    {
        Span<byte> data = BorrowFragmentData(threadAddress, _threadType);
        WritePointer(data, _threadType.GetFieldOffset("ExceptionTracker"), exceptionInfoAddress);

        if (exceptionInfoAddress == 0)
        {
            _exceptionInfosByThread.Remove(threadAddress);
        }
        else
        {
            _exceptionInfosByThread[threadAddress] = exceptionInfoAddress;
        }
    }

    public ulong SetThrownObjectHandle(ulong threadAddress, ulong objectAddress)
    {
        ulong exceptionInfoAddress = GetRequiredExceptionInfoAddress(threadAddress);
        ulong handleAddress = _allocator.AllocatePointer(objectAddress, "ThrownObjectHandle");
        Span<byte> exceptionInfoData = BorrowFragmentData(exceptionInfoAddress, _exceptionInfoType);
        WritePointer(exceptionInfoData, _exceptionInfoType.GetFieldOffset("ThrownObjectHandle"), handleAddress);
        return handleAddress;
    }

    private static ThreadDescriptorTypes CreateTypes(MockTarget.Architecture architecture)
    {
        MockDataDescriptorBuilder descriptor = new()
        {
            PointerSize = architecture.PointerSize,
        };

        return AddTypes(descriptor);
    }

    private static int GetRequiredSize(MockDataDescriptorType type)
        => checked((int)(type.Size ?? throw new InvalidOperationException("Expected descriptor type size to be populated.")));

    private ulong GetRequiredExceptionInfoAddress(ulong threadAddress)
    {
        if (_exceptionInfosByThread.TryGetValue(threadAddress, out ulong exceptionInfoAddress))
        {
            return exceptionInfoAddress;
        }

        throw new InvalidOperationException($"No exception info is associated with thread 0x{threadAddress:x}.");
    }

    private Span<byte> BorrowFragmentData(ulong address, MockDataDescriptorType type)
        => BorrowAddressRange(address, GetRequiredSize(type));

    private Span<byte> BorrowAddressRange(ulong address, int length)
    {
        foreach (MockMemorySpace.HeapFragment fragment in _allocator.Allocations)
        {
            if (address >= fragment.Address && address + (ulong)length <= fragment.Address + (ulong)fragment.Data.Length)
            {
                return fragment.Data.AsSpan((int)(address - fragment.Address), length);
            }
        }

        throw new InvalidOperationException($"No tracked fragment includes addresses from 0x{address:x} with length {length}.");
    }

    private void WriteInt32(Span<byte> destination, int offset, int value)
    {
        SpanWriter writer = new(_architecture, destination.Slice(offset, sizeof(int)));
        writer.Write(value);
    }

    private void WriteInt64(Span<byte> destination, int offset, long value)
    {
        SpanWriter writer = new(_architecture, destination.Slice(offset, sizeof(long)));
        writer.Write(unchecked((ulong)value));
    }

    private void WriteUInt32(Span<byte> destination, int offset, uint value)
    {
        SpanWriter writer = new(_architecture, destination.Slice(offset, sizeof(uint)));
        writer.Write(value);
    }

    private void WritePointer(Span<byte> destination, int offset, ulong value)
    {
        SpanWriter writer = new(_architecture, destination.Slice(offset, _architecture.PointerSize));
        writer.WritePointer(value);
    }

    private void WritePointer(Span<byte> destination, ulong value)
    {
        SpanWriter writer = new(_architecture, destination);
        writer.WritePointer(value);
    }

    private readonly record struct ThreadDescriptorTypes(
        MockDataDescriptorType ExceptionInfo,
        MockDataDescriptorType Thread,
        MockDataDescriptorType ThreadStore,
        MockDataDescriptorType RuntimeThreadLocals,
        MockDataDescriptorType EEAllocContext,
        MockDataDescriptorType GCAllocContext);

    private static ThreadDescriptorTypes AddTypes(MockDataDescriptorBuilder descriptor)
    {
        MockDataDescriptorType exceptionInfo = descriptor.AddSequentialType("ExceptionInfo", type =>
        {
            type.AddPointerField("PreviousNestedInfo");
            type.AddPointerField("ThrownObjectHandle");
            type.AddPointerField("ExceptionWatsonBucketTrackerBuckets");
        });

        MockDataDescriptorType gcAllocContext = descriptor.AddSequentialType("GCAllocContext", type =>
        {
            type.AddPointerField("Pointer");
            type.AddPointerField("Limit");
            type.AddInt64Field("AllocBytes");
            type.AddInt64Field("AllocBytesLoh");
        });

        MockDataDescriptorType eeAllocContext = descriptor.AddSequentialType("EEAllocContext", type =>
        {
            type.AddField("GCAllocationContext", GetRequiredSize(gcAllocContext), "GCAllocContext");
        });

        MockDataDescriptorType runtimeThreadLocals = descriptor.AddSequentialType("RuntimeThreadLocals", type =>
        {
            type.AddField("AllocContext", GetRequiredSize(eeAllocContext), "EEAllocContext");
        });

        MockDataDescriptorType thread = descriptor.AddSequentialType("Thread", type =>
        {
            type.AddUInt32Field("Id");
            type.AddNUIntField("OSId");
            type.AddUInt32Field("State");
            type.AddUInt32Field("PreemptiveGCDisabled");
            type.AddPointerField("RuntimeThreadLocals");
            type.AddPointerField("Frame");
            type.AddPointerField("CachedStackBase");
            type.AddPointerField("CachedStackLimit");
            type.AddPointerField("TEB");
            type.AddPointerField("LastThrownObject");
            type.AddPointerField("LinkNext");
            type.AddPointerField("ExceptionTracker");
            type.AddPointerField("ThreadLocalDataPtr");
            type.AddPointerField("UEWatsonBucketTrackerBuckets");
        });

        MockDataDescriptorType threadStore = descriptor.AddSequentialType("ThreadStore", type =>
        {
            type.AddUInt32Field("ThreadCount");
            type.AddPointerField("FirstThreadLink");
            type.AddUInt32Field("UnstartedCount");
            type.AddUInt32Field("BackgroundCount");
            type.AddUInt32Field("PendingCount");
            type.AddUInt32Field("DeadCount");
        });

        return new ThreadDescriptorTypes(exceptionInfo, thread, threadStore, runtimeThreadLocals, eeAllocContext, gcAllocContext);
    }

    internal static void AddDescriptorTypes(MockDataDescriptorBuilder descriptor)
        => AddTypes(descriptor);
}

public static class MockThreadBuilderExtensions
{
    private const string ThreadContractName = "Thread";

    public static MockProcessBuilder AddThread(
        this MockProcessBuilder processBuilder,
        Action<MockThreadBuilder> configure)
    {
        MockMemorySpace.BumpAllocator allocator = processBuilder.MemoryBuilder.DefaultAllocator;
        MockThreadBuilder config = new(allocator, processBuilder.Architecture);
        configure(config);

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptor =>
            {
                MockThreadBuilder.AddDescriptorTypes(descriptor);
                descriptor
                    .AddContract(ThreadContractName, 1)
                    .AddGlobalValue("ThreadStore", config.ThreadStoreGlobalAddress)
                    .AddGlobalValue("FinalizerThread", config.FinalizerThreadGlobalAddress)
                    .AddGlobalValue("GCThread", config.GCThreadGlobalAddress);
            });
        });

        return processBuilder;
    }
}
