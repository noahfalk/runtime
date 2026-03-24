// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

internal sealed class MockGCAllocContext : TypedView
{
    public static Layout<MockGCAllocContext> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("GCAllocContext", architecture)
            .AddPointerField("Pointer")
            .AddPointerField("Limit")
            .AddInt64Field("AllocBytes")
            .AddInt64Field("AllocBytesLoh")
            .Build<MockGCAllocContext>();

    public ulong Pointer
    {
        get => ReadPointerField("Pointer");
        set => WritePointerField("Pointer", value);
    }

    public ulong Limit
    {
        get => ReadPointerField("Limit");
        set => WritePointerField("Limit", value);
    }

    public long AllocBytes
    {
        get => ReadInt64Field("AllocBytes");
        set => WriteInt64Field("AllocBytes", value);
    }

    public long AllocBytesLoh
    {
        get => ReadInt64Field("AllocBytesLoh");
        set => WriteInt64Field("AllocBytesLoh", value);
    }
}

internal sealed class MockGeneration : TypedView
{
    public static Layout<MockGeneration> CreateLayout(MockTarget.Architecture architecture, Layout<MockGCAllocContext> allocContextLayout)
        => new SequentialLayoutBuilder("Generation", architecture)
            .AddField("AllocationContext", allocContextLayout.Size, allocContextLayout)
            .AddPointerField("StartSegment")
            .AddPointerField("AllocationStart")
            .Build<MockGeneration>();

    public MockGCAllocContext AllocationContext
        => CreateFieldView<MockGCAllocContext>("AllocationContext");

    public ulong StartSegment
    {
        get => ReadPointerField("StartSegment");
        set => WritePointerField("StartSegment", value);
    }

    public ulong AllocationStart
    {
        get => ReadPointerField("AllocationStart");
        set => WritePointerField("AllocationStart", value);
    }
}

internal sealed class MockCFinalize : TypedView
{
    public static Layout<MockCFinalize> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("CFinalize", architecture)
            .AddPointerField("FillPointers")
            .Build<MockCFinalize>();

    public void SetFillPointer(int index, ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        int offset = checked(Layout.GetField("FillPointers").Offset + (index * Architecture.PointerSize));
        WritePointer(Memory.Span.Slice(offset, Architecture.PointerSize), value);
    }
}

internal sealed class MockOomHistory : TypedView
{
    public static Layout<MockOomHistory> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("OomHistory", architecture)
            .AddUInt32Field("Reason")
            .AddNUIntField("AllocSize")
            .AddPointerField("Reserved")
            .AddPointerField("Allocated")
            .AddNUIntField("GcIndex")
            .AddUInt32Field("Fgm")
            .AddNUIntField("Size")
            .AddNUIntField("AvailablePagefileMb")
            .AddUInt32Field("LohP")
            .Build<MockOomHistory>();
}

internal sealed class MockGCHeap : TypedView
{
    internal const int InterestingDataCount = 9;
    internal const int CompactReasonsCount = 12;
    internal const int ExpandMechanismsCount = 6;
    internal const int InterestingMechanismBitsCount = 2;

    public static Layout<MockGCHeap> CreateLayout(
        MockTarget.Architecture architecture,
        Layout<MockGeneration> generationLayout,
        Layout<MockOomHistory> oomHistoryLayout,
        uint totalGenerationCount)
        => new SequentialLayoutBuilder("GCHeap", architecture)
            .AddPointerField("MarkArray")
            .AddPointerField("NextSweepObj")
            .AddPointerField("BackgroundMinSavedAddr")
            .AddPointerField("BackgroundMaxSavedAddr")
            .AddPointerField("AllocAllocated")
            .AddPointerField("EphemeralHeapSegment")
            .AddPointerField("CardTable")
            .AddPointerField("FinalizeQueue")
            .AddField("GenerationTable", checked(generationLayout.Size * (int)totalGenerationCount), generationLayout)
            .AddField("OomData", oomHistoryLayout.Size, oomHistoryLayout)
            .AddField("InterestingData", checked(architecture.PointerSize * InterestingDataCount))
            .AddField("CompactReasons", checked(architecture.PointerSize * CompactReasonsCount))
            .AddField("ExpandMechanisms", checked(architecture.PointerSize * ExpandMechanismsCount))
            .AddField("InterestingMechanismBits", checked(architecture.PointerSize * InterestingMechanismBitsCount))
            .AddPointerField("InternalRootArray")
            .AddNUIntField("InternalRootArrayIndex")
            .AddInt32Field("HeapAnalyzeSuccess")
            .Build<MockGCHeap>();

    public ulong FinalizeQueue
    {
        get => ReadPointerField("FinalizeQueue");
        set => WritePointerField("FinalizeQueue", value);
    }

    public TypedArrayView<MockGeneration> GenerationTable
    {
        get
        {
            LayoutField layoutField = Layout.GetField("GenerationTable");
            Layout<MockGeneration> generationLayout = (Layout<MockGeneration>)(layoutField.Type
                ?? throw new InvalidOperationException("GenerationTable layout is required."));
            return generationLayout.CreateArray(GetFieldMemory("GenerationTable"), GetFieldAddress("GenerationTable"));
        }
    }
}

/// <summary>
/// Configuration object for GC heap mock data, used with
/// <see cref="MockGCHeapBuilderExtensions.AddGCHeapWks"/> and
/// <see cref="MockGCHeapBuilderExtensions.AddGCHeapSvr"/>.
/// </summary>
public sealed class MockGCHeapBuilder
{
    // The native GC sizes m_FillPointers as total_generation_count + ExtraSegCount.
    private const int DefaultGenerationCount = 4;
    private const int ExtraSegCount = 2;

    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly MockTarget.Architecture _architecture;

    internal MockGCHeapBuilder(MockMemorySpace.Builder memoryBuilder)
    {
        ArgumentNullException.ThrowIfNull(memoryBuilder);

        _allocator = memoryBuilder.DefaultAllocator;
        _architecture = memoryBuilder.TargetTestHelpers.Arch;

        GCAllocContextLayout = MockGCAllocContext.CreateLayout(_architecture);
        GenerationLayout = MockGeneration.CreateLayout(_architecture, GCAllocContextLayout);
        CFinalizeLayout = MockCFinalize.CreateLayout(_architecture);
        OomHistoryLayout = MockOomHistory.CreateLayout(_architecture);
    }

    public GenerationInput[] Generations { get; set; } = new GenerationInput[DefaultGenerationCount];

    public ulong[] FillPointers { get; set; } = new ulong[DefaultGenerationCount + ExtraSegCount];

    internal Layout<MockGCAllocContext> GCAllocContextLayout { get; }

    internal Layout<MockGeneration> GenerationLayout { get; }

    internal Layout<MockCFinalize> CFinalizeLayout { get; }

    internal Layout<MockOomHistory> OomHistoryLayout { get; }

    internal Layout<MockGCHeap> GCHeapLayout
        => MockGCHeap.CreateLayout(_architecture, GenerationLayout, OomHistoryLayout, TotalGenerationCount);

    public record struct GenerationInput
    {
        public ulong StartSegment { get; set; }
        public ulong AllocationStart { get; set; }
        public ulong AllocContextPointer { get; set; }
        public ulong AllocContextLimit { get; set; }
    }

    internal uint TotalGenerationCount => checked((uint)Generations.Length);

    internal uint FillPointerCount => checked((uint)FillPointers.Length);

    internal static void PopulateGeneration(MockGeneration generationView, GenerationInput generation)
    {
        MockGCAllocContext allocContext = generationView.AllocationContext;
        allocContext.Pointer = generation.AllocContextPointer;
        allocContext.Limit = generation.AllocContextLimit;
        generationView.StartSegment = generation.StartSegment;
        generationView.AllocationStart = generation.AllocationStart;
    }

    internal TypedArrayView<MockGeneration> AllocateGenerationTable()
    {
        TypedArrayView<MockGeneration> generationTable = GenerationLayout.AllocateArray(_allocator, Generations.Length);
        for (int i = 0; i < Generations.Length; i++)
        {
            PopulateGeneration(generationTable.GetElement(i), Generations[i]);
        }

        return generationTable;
    }

    internal MockCFinalize AllocateCFinalize()
    {
        int fillPointerOffset = CFinalizeLayout.GetField("FillPointers").Offset;
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment(
            (ulong)checked(fillPointerOffset + (_architecture.PointerSize * FillPointers.Length)));
        MockCFinalize cFinalize = CFinalizeLayout.Create(fragment);
        for (int i = 0; i < FillPointers.Length; i++)
        {
            cFinalize.SetFillPointer(i, FillPointers[i]);
        }

        return cFinalize;
    }

    internal MockGCHeap AllocateGCHeap(ulong cFinalizeAddress)
    {
        MockGCHeap gcHeap = GCHeapLayout.Allocate(_allocator);
        gcHeap.FinalizeQueue = cFinalizeAddress;

        TypedArrayView<MockGeneration> generationTable = gcHeap.GenerationTable;
        for (int i = 0; i < Generations.Length; i++)
        {
            PopulateGeneration(generationTable.GetElement(i), Generations[i]);
        }

        return gcHeap;
    }
}

public static class MockGCHeapBuilderExtensions
{
    private const string GCContractName = "GC";

    public static MockProcessBuilder AddGCHeapWks(
        this MockProcessBuilder processBuilder,
        Action<MockGCHeapBuilder> configure)
    {
        MockGCHeapBuilder gcHeapBuilder = new(processBuilder.MemoryBuilder);
        configure(gcHeapBuilder);

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptorBuilder =>
            {
                TypedArrayView<MockGeneration> generationTable = gcHeapBuilder.AllocateGenerationTable();
                MockCFinalize cFinalize = gcHeapBuilder.AllocateCFinalize();
                MockOomHistory oomHistory = gcHeapBuilder.OomHistoryLayout.Allocate(processBuilder.MemoryBuilder.DefaultAllocator);
                descriptorBuilder.AddType(gcHeapBuilder.GCAllocContextLayout);
                descriptorBuilder.AddType(gcHeapBuilder.GenerationLayout);
                descriptorBuilder.AddType(gcHeapBuilder.CFinalizeLayout);
                descriptorBuilder.AddType(gcHeapBuilder.OomHistoryLayout);

                descriptorBuilder
                    .AddContract(GCContractName, 1)
                    .AddGlobalValue("TotalGenerationCount", gcHeapBuilder.TotalGenerationCount)
                    .AddGlobalValue("CFinalizeFillPointersLength", gcHeapBuilder.FillPointerCount)
                    .AddGlobalValue("InterestingDataLength", 0)
                    .AddGlobalValue("CompactReasonsLength", 0)
                    .AddGlobalValue("ExpandMechanismsLength", 0)
                    .AddGlobalValue("InterestingMechanismBitsLength", 0)
                    .AddGlobalValue("HandlesPerBlock", 32)
                    .AddGlobalValue("BlockInvalid", 1)
                    .AddGlobalValue("DebugDestroyedHandleValue", 0)
                    .AddGlobalValue("HandleMaxInternalTypes", 12)
                    .AddGlobalValue("GCHeapMarkArray", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapNextSweepObj", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapBackgroundMinSavedAddr", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapBackgroundMaxSavedAddr", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapAllocAllocated", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapEphemeralHeapSegment", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapCardTable", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapFinalizeQueue", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(cFinalize.Address))
                    .AddGlobalValue("GCHeapGenerationTable", generationTable.Address)
                    .AddGlobalValue("GCHeapOomData", oomHistory.Address)
                    .AddGlobalValue("GCHeapInternalRootArray", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapInternalRootArrayIndex", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapHeapAnalyzeSuccess", processBuilder.MemoryBuilder.DefaultAllocator.AllocateInt32(0))
                    .AddGlobalValue("GCHeapInterestingData", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapCompactReasons", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapExpandMechanisms", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapInterestingMechanismBits", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0))
                    .AddGlobalValue("GCLowestAddress", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0x1000))
                    .AddGlobalValue("GCHighestAddress", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0xFFFF_0000))
                    .AddGlobalValue("StructureInvalidCount", processBuilder.MemoryBuilder.DefaultAllocator.AllocateInt32(0))
                    .AddGlobalValue("MaxGeneration", processBuilder.MemoryBuilder.DefaultAllocator.AllocateUInt32(gcHeapBuilder.TotalGenerationCount - 1))
                    .AddGlobalString("GCIdentifiers", "workstation,segments");
            });
        });

        return processBuilder;
    }

    public static MockProcessBuilder AddGCHeapSvr(
        this MockProcessBuilder processBuilder,
        Action<MockGCHeapBuilder> configure,
        out ulong heapAddress)
    {
        MockGCHeapBuilder gcHeapBuilder = new(processBuilder.MemoryBuilder);
        configure(gcHeapBuilder);
        heapAddress = 0;
        ulong builtHeapAddress = 0;

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptorBuilder =>
            {
                MockCFinalize cFinalize = gcHeapBuilder.AllocateCFinalize();
                MockGCHeap gcHeap = gcHeapBuilder.AllocateGCHeap(cFinalize.Address);
                builtHeapAddress = gcHeap.Address;
                descriptorBuilder.AddType(gcHeapBuilder.GCAllocContextLayout);
                descriptorBuilder.AddType(gcHeapBuilder.GenerationLayout);
                descriptorBuilder.AddType(gcHeapBuilder.CFinalizeLayout);
                descriptorBuilder.AddType(gcHeapBuilder.OomHistoryLayout);
                descriptorBuilder.AddType(gcHeapBuilder.GCHeapLayout);

                descriptorBuilder
                    .AddContract(GCContractName, 1)
                    .AddGlobalValue("TotalGenerationCount", gcHeapBuilder.TotalGenerationCount)
                    .AddGlobalValue("CFinalizeFillPointersLength", gcHeapBuilder.FillPointerCount)
                    .AddGlobalValue("InterestingDataLength", 0)
                    .AddGlobalValue("CompactReasonsLength", 0)
                    .AddGlobalValue("ExpandMechanismsLength", 0)
                    .AddGlobalValue("InterestingMechanismBitsLength", 0)
                    .AddGlobalValue("HandlesPerBlock", 32)
                    .AddGlobalValue("BlockInvalid", 1)
                    .AddGlobalValue("DebugDestroyedHandleValue", 0)
                    .AddGlobalValue("HandleMaxInternalTypes", 12)
                    .AddGlobalValue("NumHeaps", processBuilder.MemoryBuilder.DefaultAllocator.AllocateUInt32(1))
                    .AddGlobalValue("Heaps", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(gcHeap.Address)))
                    .AddGlobalValue("GCLowestAddress", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0x1000))
                    .AddGlobalValue("GCHighestAddress", processBuilder.MemoryBuilder.DefaultAllocator.AllocatePointer(0x7FFF_0000))
                    .AddGlobalValue("StructureInvalidCount", processBuilder.MemoryBuilder.DefaultAllocator.AllocateInt32(0))
                    .AddGlobalValue("MaxGeneration", processBuilder.MemoryBuilder.DefaultAllocator.AllocateUInt32(gcHeapBuilder.TotalGenerationCount - 1))
                    .AddGlobalString("GCIdentifiers", "server,segments");
            });
        });

        heapAddress = builtHeapAddress;

        return processBuilder;
    }
}
