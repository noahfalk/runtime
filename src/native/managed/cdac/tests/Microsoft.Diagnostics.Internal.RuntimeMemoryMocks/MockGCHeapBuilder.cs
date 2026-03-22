// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

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

    public GenerationInput[] Generations { get; set; } = new GenerationInput[DefaultGenerationCount];
    public ulong[] FillPointers { get; set; } = new ulong[DefaultGenerationCount + ExtraSegCount];

    public record struct GenerationInput
    {
        public ulong StartSegment { get; set; }
        public ulong AllocationStart { get; set; }
        public ulong AllocContextPointer { get; set; }
        public ulong AllocContextLimit { get; set; }
    }
}

public static class MockGCHeapBuilderExtensions
{
    private const ulong DefaultAllocationRangeStart = 0x0010_0000;
    private const ulong DefaultAllocationRangeEnd = 0x0020_0000;
    private const string GCContractName = "GC";
    private const string GCAllocContextTypeName = "GCAllocContext";
    private const string GenerationTypeName = "Generation";
    private const string CFinalizeTypeName = "CFinalize";
    private const string OomHistoryTypeName = "OomHistory";
    private const string GCHeapTypeName = "GCHeap";
    private const int InterestingDataCount = 9;
    private const int CompactReasonsCount = 12;
    private const int ExpandMechanismsCount = 6;
    private const int InterestingMechanismBitsCount = 2;

    public static MockProcessBuilder AddGCHeapWks(
        this MockProcessBuilder processBuilder,
        Action<MockGCHeapBuilder> configure)
    {
        MockGCHeapBuilder config = new();
        configure(config);
        BuildWksHeap(processBuilder, config);
        return processBuilder;
    }

    public static MockProcessBuilder AddGCHeapSvr(
        this MockProcessBuilder processBuilder,
        Action<MockGCHeapBuilder> configure,
        out ulong heapAddress)
    {
        MockGCHeapBuilder config = new();
        configure(config);
        heapAddress = BuildSvrHeap(processBuilder, config);
        return processBuilder;
    }

    private static Dictionary<string, MockDataDescriptorType> GetBaseTypes(MockDataDescriptorBuilder descriptorBuilder)
    {
        MockDataDescriptorType allocContextType = descriptorBuilder.AddSequentialType(GCAllocContextTypeName, typeBuilder =>
        {
            typeBuilder.AddPointerField("Pointer");
            typeBuilder.AddPointerField("Limit");
            typeBuilder.AddInt64Field("AllocBytes");
            typeBuilder.AddInt64Field("AllocBytesLoh");
        });

        MockDataDescriptorType generationType = descriptorBuilder.AddSequentialType(GenerationTypeName, typeBuilder =>
        {
            typeBuilder.AddField("AllocationContext", checked((int)(allocContextType.Size ?? 0)));
            typeBuilder.AddPointerField("StartSegment");
            typeBuilder.AddPointerField("AllocationStart");
        });

        MockDataDescriptorType cFinalizeType = descriptorBuilder.AddSequentialType(CFinalizeTypeName, typeBuilder =>
        {
            typeBuilder.AddPointerField("FillPointers");
        });

        MockDataDescriptorType oomHistoryType = descriptorBuilder.AddSequentialType(OomHistoryTypeName, typeBuilder =>
        {
            typeBuilder.AddUInt32Field("Reason");
            typeBuilder.AddNUIntField("AllocSize");
            typeBuilder.AddPointerField("Reserved");
            typeBuilder.AddPointerField("Allocated");
            typeBuilder.AddNUIntField("GcIndex");
            typeBuilder.AddUInt32Field("Fgm");
            typeBuilder.AddNUIntField("Size");
            typeBuilder.AddNUIntField("AvailablePagefileMb");
            typeBuilder.AddUInt32Field("LohP");
        });

        return new Dictionary<string, MockDataDescriptorType>(StringComparer.Ordinal)
        {
            [GCAllocContextTypeName] = allocContextType,
            [GenerationTypeName] = generationType,
            [CFinalizeTypeName] = cFinalizeType,
            [OomHistoryTypeName] = oomHistoryType,
        };
    }

    private static Dictionary<string, MockDataDescriptorType> GetSvrTypes(
        MockDataDescriptorBuilder descriptorBuilder,
        MockTarget.Architecture architecture,
        uint totalGenerationCount)
    {
        Dictionary<string, MockDataDescriptorType> baseTypes = GetBaseTypes(descriptorBuilder);
        uint generationSize = baseTypes[GenerationTypeName].Size ?? throw new InvalidOperationException("Generation size is required.");
        uint oomHistorySize = baseTypes[OomHistoryTypeName].Size ?? throw new InvalidOperationException("OomHistory size is required.");

        int pointerSize = architecture.PointerSize;
        MockDataDescriptorType gcHeapType = descriptorBuilder.AddSequentialType(GCHeapTypeName, typeBuilder =>
        {
            typeBuilder.AddPointerField("MarkArray");
            typeBuilder.AddPointerField("NextSweepObj");
            typeBuilder.AddPointerField("BackgroundMinSavedAddr");
            typeBuilder.AddPointerField("BackgroundMaxSavedAddr");
            typeBuilder.AddPointerField("AllocAllocated");
            typeBuilder.AddPointerField("EphemeralHeapSegment");
            typeBuilder.AddPointerField("CardTable");
            typeBuilder.AddPointerField("FinalizeQueue");

            typeBuilder.AddField("GenerationTable", checked((int)(generationSize * totalGenerationCount)));
            typeBuilder.AddField("OomData", checked((int)oomHistorySize));
            typeBuilder.AddField("InterestingData", checked(pointerSize * InterestingDataCount));
            typeBuilder.AddField("CompactReasons", checked(pointerSize * CompactReasonsCount));
            typeBuilder.AddField("ExpandMechanisms", checked(pointerSize * ExpandMechanismsCount));
            typeBuilder.AddField("InterestingMechanismBits", checked(pointerSize * InterestingMechanismBitsCount));
            typeBuilder.AddPointerField("InternalRootArray");
            typeBuilder.AddNUIntField("InternalRootArrayIndex");
            typeBuilder.AddInt32Field("HeapAnalyzeSuccess");
        });

        baseTypes[GCHeapTypeName] = gcHeapType;

        return baseTypes;
    }

    private static void WriteGenerationData(
        MockTarget.Architecture architecture,
        Span<byte> generationSpan,
        Dictionary<string, MockDataDescriptorType> types,
        MockGCHeapBuilder.GenerationInput generation)
    {
        MockDataDescriptorType allocContextType = types[GCAllocContextTypeName];
        FieldWriter generationWriter = new(generationSpan, architecture, types[GenerationTypeName]);
        FieldWriter allocContextWriter = new(generationWriter.GetFieldSlice("AllocationContext"), architecture, allocContextType);

        allocContextWriter.WritePointerField("Pointer", generation.AllocContextPointer);
        allocContextWriter.WritePointerField("Limit", generation.AllocContextLimit);
        generationWriter.WritePointerField("StartSegment", generation.StartSegment);
        generationWriter.WritePointerField("AllocationStart", generation.AllocationStart);
    }

    private static ulong AllocateGenerationTable(
        MockTarget.Architecture architecture,
        MockMemorySpace.BumpAllocator allocator,
        Dictionary<string, MockDataDescriptorType> types,
        MockGCHeapBuilder.GenerationInput[] generations)
    {
        uint generationSize = types[GenerationTypeName].Size ?? throw new InvalidOperationException("Generation size is required.");
        MockMemorySpace.HeapFragment generationTable = allocator.AllocateFragment(generationSize * (uint)generations.Length);
        WriteGenerationTable(architecture, generationTable.Data, types, generations);

        return generationTable.Address;
    }

    private static void WriteGenerationTable(
        MockTarget.Architecture architecture,
        Span<byte> generationTableSpan,
        Dictionary<string, MockDataDescriptorType> types,
        MockGCHeapBuilder.GenerationInput[] generations)
    {
        uint generationSize = types[GenerationTypeName].Size ?? throw new InvalidOperationException("Generation size is required.");
        for (int i = 0; i < generations.Length; i++)
        {
            WriteGenerationData(
                architecture,
                generationTableSpan.Slice((int)(i * generationSize), (int)generationSize),
                types,
                generations[i]);
        }
    }

    private static ulong AllocateCFinalize(
        MockTarget.Architecture architecture,
        MockMemorySpace.BumpAllocator allocator,
        MockDataDescriptorType cFinalizeType,
        ulong[] fillPointers)
    {
        int fillPointerOffset = cFinalizeType.GetFieldOffset("FillPointers");
        ulong cFinalizeSize = (ulong)fillPointerOffset + (ulong)(architecture.PointerSize * fillPointers.Length);
        MockMemorySpace.HeapFragment cFinalize = allocator.AllocateFragment(cFinalizeSize);
        SpanWriter writer = new(architecture, cFinalize.Data.AsSpan(fillPointerOffset));
        foreach (ulong fillPointer in fillPointers)
        {
            writer.WritePointer(fillPointer);
        }
        return cFinalize.Address;
    }

    private static ulong AllocateGCHeap(
        MockTarget.Architecture architecture,
        MockMemorySpace.BumpAllocator allocator,
        Dictionary<string, MockDataDescriptorType> types,
        MockGCHeapBuilder.GenerationInput[] generations,
        ulong cFinalizeAddress)
    {
        MockDataDescriptorType gcHeapType = types[GCHeapTypeName];
        MockMemorySpace.HeapFragment gcHeap = allocator.AllocateFragment(gcHeapType.Size ?? 0);
        FieldWriter gcHeapWriter = new(gcHeap.Data.AsSpan(), architecture, gcHeapType);
        gcHeapWriter.WritePointerField("FinalizeQueue", cFinalizeAddress);
        WriteGenerationTable(
            architecture,
            gcHeapWriter.GetFieldSlice("GenerationTable"),
            types,
            generations);
        return gcHeap.Address;
    }

    private static void BuildWksHeap(MockProcessBuilder processBuilder, MockGCHeapBuilder config)
    {
        MockMemorySpace.Builder memoryBuilder = processBuilder.MemoryBuilder;
        MockTarget.Architecture architecture = processBuilder.Architecture;
        MockMemorySpace.BumpAllocator allocator = memoryBuilder.CreateAllocator(DefaultAllocationRangeStart, DefaultAllocationRangeEnd);
        MockGCHeapBuilder.GenerationInput[] generations = config.Generations;
        uint generationCount = (uint)generations.Length;
        ulong[] fillPointers = config.FillPointers;
        uint fillPointerCount = (uint)fillPointers.Length;

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptorBuilder =>
            {
                Dictionary<string, MockDataDescriptorType> types = GetBaseTypes(descriptorBuilder);
                ulong generationTableAddress = AllocateGenerationTable(architecture, allocator, types, generations);
                ulong cFinalizeAddress = AllocateCFinalize(architecture, allocator, types[CFinalizeTypeName], fillPointers);
                ulong oomHistoryAddress = allocator.AllocateFragment(types[OomHistoryTypeName].Size ?? 0).Address;

                descriptorBuilder
                    .AddContract(GCContractName, 1)
                    .AddGlobalValue("TotalGenerationCount", generationCount)
                    .AddGlobalValue("CFinalizeFillPointersLength", fillPointerCount)
                    .AddGlobalValue("InterestingDataLength", 0)
                    .AddGlobalValue("CompactReasonsLength", 0)
                    .AddGlobalValue("ExpandMechanismsLength", 0)
                    .AddGlobalValue("InterestingMechanismBitsLength", 0)
                    .AddGlobalValue("HandlesPerBlock", 32)
                    .AddGlobalValue("BlockInvalid", 1)
                    .AddGlobalValue("DebugDestroyedHandleValue", 0)
                    .AddGlobalValue("HandleMaxInternalTypes", 12)
                    .AddGlobalValue("GCHeapMarkArray", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapNextSweepObj", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapBackgroundMinSavedAddr", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapBackgroundMaxSavedAddr", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapAllocAllocated", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapEphemeralHeapSegment", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapCardTable", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapFinalizeQueue", allocator.AllocatePointer(cFinalizeAddress))
                    .AddGlobalValue("GCHeapGenerationTable", generationTableAddress)
                    .AddGlobalValue("GCHeapOomData", oomHistoryAddress)
                    .AddGlobalValue("GCHeapInternalRootArray", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapInternalRootArrayIndex", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapHeapAnalyzeSuccess", allocator.AllocateInt32(0))
                    .AddGlobalValue("GCHeapInterestingData", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapCompactReasons", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapExpandMechanisms", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCHeapInterestingMechanismBits", allocator.AllocatePointer(0))
                    .AddGlobalValue("GCLowestAddress", allocator.AllocatePointer(0x1000))
                    .AddGlobalValue("GCHighestAddress", allocator.AllocatePointer(0xFFFF_0000))
                    .AddGlobalValue("StructureInvalidCount", allocator.AllocateInt32(0))
                    .AddGlobalValue("MaxGeneration", allocator.AllocateUInt32(generationCount - 1))
                    .AddGlobalString("GCIdentifiers", "workstation,segments");
            });
        });
    }

    private static ulong BuildSvrHeap(MockProcessBuilder processBuilder, MockGCHeapBuilder config)
    {
        MockMemorySpace.Builder memoryBuilder = processBuilder.MemoryBuilder;
        MockTarget.Architecture architecture = processBuilder.Architecture;
        MockMemorySpace.BumpAllocator allocator = memoryBuilder.CreateAllocator(DefaultAllocationRangeStart, DefaultAllocationRangeEnd);
        ulong heapAddress = 0;
        MockGCHeapBuilder.GenerationInput[] generations = config.Generations;
        uint generationCount = (uint)generations.Length;
        ulong[] fillPointers = config.FillPointers;
        uint fillPointerCount = (uint)fillPointers.Length;

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptorBuilder =>
            {
                Dictionary<string, MockDataDescriptorType> types = GetSvrTypes(descriptorBuilder, architecture, generationCount);
                ulong cFinalizeAddress = AllocateCFinalize(architecture, allocator, types[CFinalizeTypeName], fillPointers);
                heapAddress = AllocateGCHeap(architecture, allocator, types, generations, cFinalizeAddress);
                descriptorBuilder
                    .AddContract(GCContractName, 1)
                    .AddGlobalValue("TotalGenerationCount", generationCount)
                    .AddGlobalValue("CFinalizeFillPointersLength", fillPointerCount)
                    .AddGlobalValue("InterestingDataLength", 0)
                    .AddGlobalValue("CompactReasonsLength", 0)
                    .AddGlobalValue("ExpandMechanismsLength", 0)
                    .AddGlobalValue("InterestingMechanismBitsLength", 0)
                    .AddGlobalValue("HandlesPerBlock", 32)
                    .AddGlobalValue("BlockInvalid", 1)
                    .AddGlobalValue("DebugDestroyedHandleValue", 0)
                    .AddGlobalValue("HandleMaxInternalTypes", 12)
                    .AddGlobalValue("NumHeaps", allocator.AllocateUInt32(1))
                    .AddGlobalValue("Heaps", allocator.AllocatePointer(allocator.AllocatePointer(heapAddress)))
                    .AddGlobalValue("GCLowestAddress", allocator.AllocatePointer(0x1000))
                    .AddGlobalValue("GCHighestAddress", allocator.AllocatePointer(0x7FFF_0000))
                    .AddGlobalValue("StructureInvalidCount", allocator.AllocateInt32(0))
                    .AddGlobalValue("MaxGeneration", allocator.AllocateUInt32(generationCount - 1))
                    .AddGlobalString("GCIdentifiers", "server,segments");
            });
        });

        return heapAddress;
    }
}
