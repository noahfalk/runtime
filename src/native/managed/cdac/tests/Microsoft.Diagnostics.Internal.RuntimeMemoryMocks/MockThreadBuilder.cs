// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

internal sealed class MockExceptionInfo : TypedView
{
    public static Layout<MockExceptionInfo> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("ExceptionInfo", architecture)
            .AddPointerField("PreviousNestedInfo")
            .AddPointerField("ThrownObjectHandle")
            .AddPointerField("ExceptionWatsonBucketTrackerBuckets")
            .Build<MockExceptionInfo>();

    public ulong ThrownObjectHandle
    {
        get => ReadPointerField("ThrownObjectHandle");
        set => WritePointerField("ThrownObjectHandle", value);
    }
}

internal sealed class MockEEAllocContext : TypedView
{
    public static Layout<MockEEAllocContext> CreateLayout(MockTarget.Architecture architecture, Layout<MockGCAllocContext> gcAllocContextLayout)
        => new SequentialLayoutBuilder("EEAllocContext", architecture)
            .AddField("GCAllocationContext", gcAllocContextLayout.Size, gcAllocContextLayout)
            .Build<MockEEAllocContext>();

    public MockGCAllocContext GCAllocationContext
        => CreateFieldView<MockGCAllocContext>("GCAllocationContext");
}

internal sealed class MockRuntimeThreadLocals : TypedView
{
    public static Layout<MockRuntimeThreadLocals> CreateLayout(MockTarget.Architecture architecture, Layout<MockEEAllocContext> eeAllocContextLayout)
        => new SequentialLayoutBuilder("RuntimeThreadLocals", architecture)
            .AddField("AllocContext", eeAllocContextLayout.Size, eeAllocContextLayout)
            .Build<MockRuntimeThreadLocals>();

    public MockEEAllocContext AllocContext
        => CreateFieldView<MockEEAllocContext>("AllocContext");
}

internal sealed class MockThreadData : TypedView
{
    public static Layout<MockThreadData> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("Thread", architecture)
            .AddUInt32Field("Id")
            .AddNUIntField("OSId")
            .AddUInt32Field("State")
            .AddUInt32Field("PreemptiveGCDisabled")
            .AddPointerField("RuntimeThreadLocals")
            .AddPointerField("Frame")
            .AddPointerField("CachedStackBase")
            .AddPointerField("CachedStackLimit")
            .AddPointerField("TEB")
            .AddPointerField("LastThrownObject")
            .AddPointerField("LinkNext")
            .AddPointerField("ExceptionTracker")
            .AddPointerField("ThreadLocalDataPtr")
            .AddPointerField("UEWatsonBucketTrackerBuckets")
            .Build<MockThreadData>();

    public uint Id
    {
        get => ReadUInt32Field("Id");
        set => WriteUInt32Field("Id", value);
    }

    public ulong OSId
    {
        get => ReadPointerField("OSId");
        set => WritePointerField("OSId", value);
    }

    public ulong RuntimeThreadLocals
    {
        get => ReadPointerField("RuntimeThreadLocals");
        set => WritePointerField("RuntimeThreadLocals", value);
    }

    public ulong CachedStackBase
    {
        get => ReadPointerField("CachedStackBase");
        set => WritePointerField("CachedStackBase", value);
    }

    public ulong CachedStackLimit
    {
        get => ReadPointerField("CachedStackLimit");
        set => WritePointerField("CachedStackLimit", value);
    }

    public ulong LinkNext
    {
        get => ReadPointerField("LinkNext");
        set => WritePointerField("LinkNext", value);
    }

    public ulong ExceptionTracker
    {
        get => ReadPointerField("ExceptionTracker");
        set => WritePointerField("ExceptionTracker", value);
    }
}

internal sealed class MockThreadStore : TypedView
{
    public static Layout<MockThreadStore> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("ThreadStore", architecture)
            .AddUInt32Field("ThreadCount")
            .AddPointerField("FirstThreadLink")
            .AddUInt32Field("UnstartedCount")
            .AddUInt32Field("BackgroundCount")
            .AddUInt32Field("PendingCount")
            .AddUInt32Field("DeadCount")
            .Build<MockThreadStore>();

    public int ThreadCount
    {
        get => ReadInt32Field("ThreadCount");
        set => WriteInt32Field("ThreadCount", value);
    }

    public ulong FirstThreadLink
    {
        get => ReadPointerField("FirstThreadLink");
        set => WritePointerField("FirstThreadLink", value);
    }

    public int UnstartedCount
    {
        get => ReadInt32Field("UnstartedCount");
        set => WriteInt32Field("UnstartedCount", value);
    }

    public int BackgroundCount
    {
        get => ReadInt32Field("BackgroundCount");
        set => WriteInt32Field("BackgroundCount", value);
    }

    public int PendingCount
    {
        get => ReadInt32Field("PendingCount");
        set => WriteInt32Field("PendingCount", value);
    }

    public int DeadCount
    {
        get => ReadInt32Field("DeadCount");
        set => WriteInt32Field("DeadCount", value);
    }
}

public sealed class MockThreadBuilder
{
    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly Dictionary<ulong, ulong> _exceptionInfosByThread = [];
    private readonly Dictionary<ulong, MockThreadData> _threads = [];

    private ulong _previousThreadAddress;
    private readonly MockThreadStore _threadStore;
    private readonly MockThreadData _finalizerThread;
    private readonly MockThreadData _gcThread;

    internal MockThreadBuilder(MockMemorySpace.BumpAllocator allocator, MockTarget.Architecture architecture)
    {
        _allocator = allocator;

        ExceptionInfoLayout = MockExceptionInfo.CreateLayout(architecture);
        GCAllocContextLayout = MockGCAllocContext.CreateLayout(architecture);
        EEAllocContextLayout = MockEEAllocContext.CreateLayout(architecture, GCAllocContextLayout);
        RuntimeThreadLocalsLayout = MockRuntimeThreadLocals.CreateLayout(architecture, EEAllocContextLayout);
        ThreadLayout = MockThreadData.CreateLayout(architecture);
        ThreadStoreLayout = MockThreadStore.CreateLayout(architecture);

        _threadStore = ThreadStoreLayout.Allocate(_allocator, "ThreadStore");
        ThreadStoreGlobalAddress = _allocator.AllocatePointer(_threadStore.Address, "[global pointer] ThreadStore");

        _finalizerThread = ThreadLayout.Allocate(_allocator, "Finalizer thread");
        FinalizerThreadGlobalAddress = _allocator.AllocatePointer(_finalizerThread.Address, "[global pointer] Finalizer thread");

        _gcThread = ThreadLayout.Allocate(_allocator, "GC thread");
        GCThreadGlobalAddress = _allocator.AllocatePointer(_gcThread.Address, "[global pointer] GC thread");
    }

    public ulong ThreadStoreGlobalAddress { get; }

    public ulong FinalizerThreadGlobalAddress { get; }

    public ulong FinalizerThreadAddress => _finalizerThread.Address;

    public ulong GCThreadGlobalAddress { get; }

    public ulong GCThreadAddress => _gcThread.Address;

    internal Layout<MockExceptionInfo> ExceptionInfoLayout { get; }

    internal Layout<MockThreadData> ThreadLayout { get; }

    internal Layout<MockThreadStore> ThreadStoreLayout { get; }

    internal Layout<MockRuntimeThreadLocals> RuntimeThreadLocalsLayout { get; }

    internal Layout<MockEEAllocContext> EEAllocContextLayout { get; }

    internal Layout<MockGCAllocContext> GCAllocContextLayout { get; }

    public void SetThreadCounts(int threadCount, int unstartedCount, int backgroundCount, int pendingCount, int deadCount)
    {
        _threadStore.ThreadCount = threadCount;
        _threadStore.UnstartedCount = unstartedCount;
        _threadStore.BackgroundCount = backgroundCount;
        _threadStore.PendingCount = pendingCount;
        _threadStore.DeadCount = deadCount;
    }

    public ulong AddThread(uint id, ulong osId)
        => AddThread(id, osId, allocBytes: 0, allocBytesLoh: 0);

    public ulong AddThread(uint id, ulong osId, long allocBytes, long allocBytesLoh)
    {
        MockExceptionInfo exceptionInfo = ExceptionInfoLayout.Allocate(_allocator, "ExceptionInfo");
        MockRuntimeThreadLocals runtimeThreadLocals = RuntimeThreadLocalsLayout.Allocate(_allocator, "RuntimeThreadLocals");
        MockThreadData thread = ThreadLayout.Allocate(_allocator, "Thread");

        thread.Id = id;
        thread.OSId = osId;
        thread.ExceptionTracker = exceptionInfo.Address;
        thread.RuntimeThreadLocals = runtimeThreadLocals.Address;

        MockGCAllocContext gcAllocContext = runtimeThreadLocals.AllocContext.GCAllocationContext;
        gcAllocContext.AllocBytes = allocBytes;
        gcAllocContext.AllocBytesLoh = allocBytesLoh;

        ulong threadLinkAddress = thread.Address + (ulong)ThreadLayout.GetField("LinkNext").Offset;
        if (_previousThreadAddress == 0)
        {
            _threadStore.FirstThreadLink = threadLinkAddress;
        }
        else
        {
            _threads[_previousThreadAddress].LinkNext = threadLinkAddress;
        }

        _previousThreadAddress = thread.Address;
        _threads[thread.Address] = thread;
        _exceptionInfosByThread[thread.Address] = exceptionInfo.Address;
        return thread.Address;
    }

    public void SetStackLimits(ulong threadAddress, ulong stackBase, ulong stackLimit)
    {
        MockThreadData thread = GetRequiredThread(threadAddress);
        thread.CachedStackBase = stackBase;
        thread.CachedStackLimit = stackLimit;
    }

    public void SetExceptionTracker(ulong threadAddress, ulong exceptionInfoAddress)
    {
        MockThreadData thread = GetRequiredThread(threadAddress);
        thread.ExceptionTracker = exceptionInfoAddress;

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
        MockExceptionInfo exceptionInfo = GetRequiredExceptionInfo(threadAddress);
        ulong handleAddress = _allocator.AllocatePointer(objectAddress, "ThrownObjectHandle");
        exceptionInfo.ThrownObjectHandle = handleAddress;
        return handleAddress;
    }

    private MockThreadData GetRequiredThread(ulong threadAddress)
    {
        if (_threads.TryGetValue(threadAddress, out MockThreadData? thread))
        {
            return thread;
        }

        throw new InvalidOperationException($"No thread is associated with address 0x{threadAddress:x}.");
    }

    private MockExceptionInfo GetRequiredExceptionInfo(ulong threadAddress)
    {
        if (_exceptionInfosByThread.TryGetValue(threadAddress, out ulong exceptionInfoAddress))
        {
            return CreateViewAtAddress(exceptionInfoAddress, ExceptionInfoLayout);
        }

        throw new InvalidOperationException($"No exception info is associated with thread 0x{threadAddress:x}.");
    }

    private TView CreateViewAtAddress<TView>(ulong address, Layout<TView> layout)
        where TView : TypedView, new()
    {
        foreach (MockMemorySpace.HeapFragment fragment in _allocator.Allocations)
        {
            ulong fragmentEnd = fragment.Address + (ulong)fragment.Data.Length;
            ulong requestedEnd = address + (ulong)layout.Size;
            if (address >= fragment.Address && requestedEnd <= fragmentEnd)
            {
                int offset = checked((int)(address - fragment.Address));
                return layout.Create(fragment.Data.AsMemory(offset, layout.Size), address);
            }
        }

        throw new InvalidOperationException($"No tracked fragment includes addresses from 0x{address:x} with length {layout.Size}.");
    }
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
                descriptor.AddType(config.ExceptionInfoLayout);
                descriptor.AddType(config.GCAllocContextLayout);
                descriptor.AddType(config.EEAllocContextLayout);
                descriptor.AddType(config.RuntimeThreadLocalsLayout);
                descriptor.AddType(config.ThreadLayout);
                descriptor.AddType(config.ThreadStoreLayout);
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
