// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;
using Xunit;

namespace Microsoft.Diagnostics.DataContractReader.Tests;

public sealed class MockProcessBuilderTests
{
    private const ulong ModuleRegionStart = 0x5000_0000;
    private const ulong ModuleRegionEnd = ModuleRegionStart + 0x1000_0000;

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void AddModuleAndDataDescriptor_AccumulateConfigurations(MockTarget.Architecture arch)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddCoreClr(module =>
            {
                module.AddDataDescriptor(descriptor =>
                {
                    descriptor
                        .AddGlobalValue("NumericGlobal", 0x1234)
                        .AddType("CustomType", type =>
                        {
                            type.Size = 16;
                        });
                });
            })
            .AddCoreClr(module =>
            {
                module.AddDataDescriptor(descriptor =>
                {
                    descriptor
                        .AddGlobalString("StringGlobal", "hello")
                        .AddType("CustomType", type =>
                        {
                            type.AddField("Field1", 8);
                        });
                });
            })
            .Build();

        MockLoadedModule module = Assert.Single(process.LoadedModules);
        Assert.InRange(module.BaseAddress, ModuleRegionStart, ModuleRegionEnd - 1);
        Assert.True(module.Size > 0);
        Assert.True(module.TryGetExport("DotNetRuntimeContractDescriptor", out MockModuleExport? export));
        Assert.NotNull(export);

        ContractDescriptorTarget target = process.CreateContractDescriptorTarget();

        Assert.Equal((ulong)0x1234, target.ReadGlobal<ulong>("NumericGlobal"));
        Assert.Equal("hello", target.ReadGlobalString("StringGlobal"));

        Target.TypeInfo typeInfo = target.GetTypeInfo("CustomType");
        Assert.Equal((uint)16, typeInfo.Size);
        Assert.Equal(8, typeInfo.Fields["Field1"].Offset);
        Assert.Null(typeInfo.Fields["Field1"].TypeName);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void AddModule_EmptyModulesGetDistinctRanges(MockTarget.Architecture arch)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddModule("a.dll", static _ => { })
            .AddModule("b.dll", static _ => { })
            .Build();

        Assert.Collection(
            process.LoadedModules,
            first =>
            {
                Assert.Equal("a.dll", first.Name);
                Assert.Equal(ModuleRegionStart, first.BaseAddress);
                Assert.Equal((ulong)0x1000, first.Size);
            },
            second =>
            {
                Assert.Equal("b.dll", second.Name);
                Assert.Equal(ModuleRegionStart + 0x1000, second.BaseAddress);
                Assert.Equal((ulong)0x1000, second.Size);
            });
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void AddSequentialType_AlignsFieldsAndAccumulatesUpdates(MockTarget.Architecture arch)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddCoreClr(module =>
            {
                module.AddDataDescriptor(descriptor =>
                {
                    descriptor.AddSequentialType("SequentialType", type =>
                    {
                        type.AddField("ByteField", 1);
                        type.AddPointerField("LargeField");
                    });
                });
            })
            .AddCoreClr(module =>
            {
                module.AddDataDescriptor(descriptor =>
                {
                    descriptor.AddSequentialType("SequentialType", type =>
                    {
                        type.AddInt32Field("IntField");
                    });
                });
            })
            .Build();

        ContractDescriptorTarget target = process.CreateContractDescriptorTarget();
        Target.TypeInfo typeInfo = target.GetTypeInfo("SequentialType");

        int largeFieldOffset = arch.PointerSize;
        int intFieldOffset = arch.Is64Bit ? 16 : 8;
        uint expectedSize = arch.Is64Bit ? 20u : 12u;

        Assert.Equal(expectedSize, typeInfo.Size);
        Assert.Equal(0, typeInfo.Fields["ByteField"].Offset);
        Assert.Equal(largeFieldOffset, typeInfo.Fields["LargeField"].Offset);
        Assert.Equal(intFieldOffset, typeInfo.Fields["IntField"].Offset);
        Assert.Null(typeInfo.Fields["IntField"].TypeName);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void AddSequentialType_CommonFieldHelpersUseExpectedSizes(MockTarget.Architecture arch)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddCoreClr(module =>
            {
                module.AddDataDescriptor(descriptor =>
                {
                    descriptor.AddSequentialType("HelperType", type =>
                    {
                        type.AddInt32Field("IntField");
                        type.AddUInt32Field("UIntField");
                        type.AddPointerField("PointerField");
                    });
                });
            })
            .Build();

        ContractDescriptorTarget target = process.CreateContractDescriptorTarget();
        Target.TypeInfo typeInfo = target.GetTypeInfo("HelperType");

        int pointerFieldOffset = arch.Is64Bit ? 8 : 8;
        uint expectedSize = arch.Is64Bit ? 16u : 12u;

        Assert.Equal(expectedSize, typeInfo.Size);
        Assert.Equal(0, typeInfo.Fields["IntField"].Offset);
        Assert.Equal(4, typeInfo.Fields["UIntField"].Offset);
        Assert.Equal(pointerFieldOffset, typeInfo.Fields["PointerField"].Offset);
        Assert.Null(typeInfo.Fields["IntField"].TypeName);
        Assert.Null(typeInfo.Fields["UIntField"].TypeName);
        Assert.Null(typeInfo.Fields["PointerField"].TypeName);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void AddGCHeapSvr_UsesDistinctSequentialInterestingInfoOffsets(MockTarget.Architecture arch)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddGCHeapSvr(static _ => { }, out _)
            .Build();

        ContractDescriptorTarget target = process.CreateContractDescriptorTarget();
        Target.TypeInfo typeInfo = target.GetTypeInfo("GCHeap");

        int interestingDataOffset = typeInfo.Fields["InterestingData"].Offset;
        int compactReasonsOffset = typeInfo.Fields["CompactReasons"].Offset;
        int expandMechanismsOffset = typeInfo.Fields["ExpandMechanisms"].Offset;
        int interestingMechanismBitsOffset = typeInfo.Fields["InterestingMechanismBits"].Offset;
        int internalRootArrayOffset = typeInfo.Fields["InternalRootArray"].Offset;
        int internalRootArrayIndexOffset = typeInfo.Fields["InternalRootArrayIndex"].Offset;
        int heapAnalyzeSuccessOffset = typeInfo.Fields["HeapAnalyzeSuccess"].Offset;

        Assert.Equal(interestingDataOffset + (9 * arch.PointerSize), compactReasonsOffset);
        Assert.Equal(compactReasonsOffset + (12 * arch.PointerSize), expandMechanismsOffset);
        Assert.Equal(expandMechanismsOffset + (6 * arch.PointerSize), interestingMechanismBitsOffset);
        Assert.Equal(interestingMechanismBitsOffset + (2 * arch.PointerSize), internalRootArrayOffset);
        Assert.Equal(internalRootArrayOffset + arch.PointerSize, internalRootArrayIndexOffset);
        Assert.Equal(internalRootArrayIndexOffset + arch.PointerSize, heapAnalyzeSuccessOffset);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void DefaultAllocator_TracksFragmentsButCreateUntrackedAllocatorDoesNot(MockTarget.Architecture arch)
    {
        MockMemorySpace.Builder builder = new(new MockMemoryHelpers(arch));

        MockMemorySpace.BumpAllocator trackedAllocator = builder.DefaultAllocator;
        MockMemorySpace.HeapFragment trackedFragment = trackedAllocator.AllocateFragment(8, "tracked");

        Span<byte> trackedBytes = builder.BorrowAddressRange(trackedFragment.Address, trackedFragment.Data.Length);
        Assert.Equal(trackedFragment.Data.Length, trackedBytes.Length);

        MockMemorySpace.BumpAllocator untrackedAllocator = builder.CreateUntrackedAllocator(0x3000, 0x4000);
        MockMemorySpace.HeapFragment untrackedFragment = untrackedAllocator.AllocateFragment(8, "untracked");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => builder.BorrowAddressRange(untrackedFragment.Address, untrackedFragment.Data.Length));
        Assert.Contains("No fragment includes addresses", ex.Message);

        builder.AddHeapFragment(untrackedFragment);
        Span<byte> untrackedBytes = builder.BorrowAddressRange(untrackedFragment.Address, untrackedFragment.Data.Length);
        Assert.Equal(untrackedFragment.Data.Length, untrackedBytes.Length);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void BumpAllocator_PrimitiveHelpersWriteInitialValues(MockTarget.Architecture arch)
    {
        MockMemorySpace.Builder builder = new(new MockMemoryHelpers(arch));
        MockMemorySpace.BumpAllocator allocator = builder.DefaultAllocator;

        ulong pointerAddress = allocator.AllocatePointer(0x1234, "ptr");
        ulong int32Address = allocator.AllocateInt32(-7, "i32");
        ulong uint32Address = allocator.AllocateUInt32(42, "u32");

        MockMemoryHelpers helpers = new(arch);
        Span<byte> pointerBytes = builder.BorrowAddressRange(pointerAddress, helpers.PointerSize);
        Span<byte> int32Bytes = builder.BorrowAddressRange(int32Address, sizeof(int));
        Span<byte> uint32Bytes = builder.BorrowAddressRange(uint32Address, sizeof(uint));

        ulong pointerValue = arch.Is64Bit
            ? (arch.IsLittleEndian
                ? BinaryPrimitives.ReadUInt64LittleEndian(pointerBytes)
                : BinaryPrimitives.ReadUInt64BigEndian(pointerBytes))
            : (arch.IsLittleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(pointerBytes)
                : BinaryPrimitives.ReadUInt32BigEndian(pointerBytes));
        int int32Value = arch.IsLittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(int32Bytes)
            : BinaryPrimitives.ReadInt32BigEndian(int32Bytes);
        uint uint32Value = arch.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(uint32Bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(uint32Bytes);

        Assert.Equal(helpers.PointerSize, pointerBytes.Length);
        Assert.Equal((ulong)0x1234, pointerValue);
        Assert.Equal(-7, int32Value);
        Assert.Equal((uint)42, uint32Value);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void AddBuiltInCom_AddsExpectedTypesAndGlobals(MockTarget.Architecture arch)
    {
        MockBuiltInComBuilder? builtInCom = null;
        MockProcess process = new MockProcessBuilder(arch)
            .AddBuiltInCom(config => builtInCom = config)
            .Build();

        ContractDescriptorTarget target = process.CreateContractDescriptorTarget();
        Assert.NotNull(builtInCom);

        Assert.Equal((uint)MockBuiltInComBuilder.DefaultNumVtablePtrs, target.ReadGlobal<uint>("CCWNumInterfaces"));
        Assert.Equal((uint)MockBuiltInComBuilder.DefaultRCWInterfaceCacheSize, target.ReadGlobal<uint>("RCWInterfaceCacheSize"));
        Assert.Equal(
            arch.Is64Bit ? ~0x3FUL : ~0x1FUL,
            target.ReadGlobal<ulong>("CCWThisMask"));
        Assert.Equal(builtInCom.TearOffAddRefSlot, target.ReadGlobal<ulong>("TearOffAddRef"));
        Assert.Equal(builtInCom.TearOffAddRefSimpleSlot, target.ReadGlobal<ulong>("TearOffAddRefSimple"));
        Assert.Equal(builtInCom.TearOffAddRefSimpleInnerSlot, target.ReadGlobal<ulong>("TearOffAddRefSimpleInner"));

        Assert.Equal(builtInCom.TearOffAddRefAddress, (ulong)target.ReadPointer(builtInCom.TearOffAddRefSlot));
        Assert.Equal(builtInCom.TearOffAddRefSimpleAddress, (ulong)target.ReadPointer(builtInCom.TearOffAddRefSimpleSlot));
        Assert.Equal(builtInCom.TearOffAddRefSimpleInnerAddress, (ulong)target.ReadPointer(builtInCom.TearOffAddRefSimpleInnerSlot));

        Target.TypeInfo comCallWrapper = target.GetTypeInfo("ComCallWrapper");
        Target.TypeInfo simpleComCallWrapper = target.GetTypeInfo("SimpleComCallWrapper");
        Target.TypeInfo comMethodTable = target.GetTypeInfo("ComMethodTable");
        Target.TypeInfo interfaceEntry = target.GetTypeInfo("InterfaceEntry");
        Target.TypeInfo ctxEntry = target.GetTypeInfo("CtxEntry");
        Target.TypeInfo rcw = target.GetTypeInfo("RCW");

        Assert.Equal(0, comCallWrapper.Fields["SimpleWrapper"].Offset);
        Assert.Equal(arch.PointerSize, comCallWrapper.Fields["IPtr"].Offset);
        Assert.Equal(6 * arch.PointerSize, comCallWrapper.Fields["Next"].Offset);
        Assert.Equal(7 * arch.PointerSize, comCallWrapper.Fields["Handle"].Offset);

        Assert.Equal(0, simpleComCallWrapper.Fields["RefCount"].Offset);
        Assert.Equal(8, simpleComCallWrapper.Fields["Flags"].Offset);
        Assert.Equal(12, simpleComCallWrapper.Fields["MainWrapper"].Offset);
        Assert.Equal(12 + arch.PointerSize, simpleComCallWrapper.Fields["VTablePtr"].Offset);
        Assert.Equal(12 + (2 * arch.PointerSize), simpleComCallWrapper.Fields["OuterIUnknown"].Offset);

        Assert.Equal((uint)(2 * arch.PointerSize), comMethodTable.Size);
        Assert.Equal(0, comMethodTable.Fields["Flags"].Offset);
        Assert.Equal(arch.PointerSize, comMethodTable.Fields["MethodTable"].Offset);

        Assert.Equal(0, interfaceEntry.Fields["MethodTable"].Offset);
        Assert.Equal(arch.PointerSize, interfaceEntry.Fields["Unknown"].Offset);

        Assert.Equal(0, ctxEntry.Fields["STAThread"].Offset);
        Assert.Equal(arch.PointerSize, ctxEntry.Fields["CtxCookie"].Offset);

        Assert.Equal(0, rcw.Fields["NextCleanupBucket"].Offset);
        Assert.Equal(arch.PointerSize, rcw.Fields["NextRCW"].Offset);
        Assert.Equal(2 * arch.PointerSize, rcw.Fields["Flags"].Offset);
        Assert.Equal(arch.Is64Bit ? 24 : 12, rcw.Fields["CtxCookie"].Offset);
        Assert.Equal(arch.Is64Bit ? 32 : 16, rcw.Fields["CtxEntry"].Offset);
        Assert.Equal(arch.Is64Bit ? 40 : 20, rcw.Fields["InterfaceEntries"].Offset);
        Assert.Equal(arch.Is64Bit ? 48 : 24, rcw.Fields["IdentityPointer"].Offset);
        Assert.Equal(arch.Is64Bit ? 56 : 28, rcw.Fields["SyncBlockIndex"].Offset);
        Assert.Equal(arch.Is64Bit ? 64 : 32, rcw.Fields["VTablePtr"].Offset);
        Assert.Equal(arch.Is64Bit ? 72 : 36, rcw.Fields["CreatorThread"].Offset);
        Assert.Equal(arch.Is64Bit ? 80 : 40, rcw.Fields["RefCount"].Offset);
        Assert.Equal(arch.Is64Bit ? 88 : 44, rcw.Fields["UnknownPointer"].Offset);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void AddSyncBlock_AddsExpectedTypesAndGlobals(MockTarget.Architecture arch)
    {
        MockSyncBlockBuilder? syncBlock = null;
        MockProcess process = new MockProcessBuilder(arch)
            .AddSyncBlock(config => syncBlock = config)
            .Build();

        ContractDescriptorTarget target = process.CreateContractDescriptorTarget();
        Assert.NotNull(syncBlock);

        Assert.Equal(syncBlock.SyncBlockCacheGlobalAddress, target.ReadGlobal<ulong>("SyncBlockCache"));
        Assert.Equal(syncBlock.SyncTableEntriesGlobalAddress, target.ReadGlobal<ulong>("SyncTableEntries"));

        Target.TypeInfo syncBlockCache = target.GetTypeInfo("SyncBlockCache");
        Target.TypeInfo syncTableEntry = target.GetTypeInfo("SyncTableEntry");
        Target.TypeInfo syncBlockType = target.GetTypeInfo("SyncBlock");
        Target.TypeInfo interopSyncBlockInfo = target.GetTypeInfo("InteropSyncBlockInfo");

        Assert.Equal(0, syncBlockCache.Fields["FreeSyncTableIndex"].Offset);
        Assert.Equal(arch.Is64Bit ? 8 : 4, syncBlockCache.Fields["CleanupBlockList"].Offset);

        Assert.Equal(0, syncTableEntry.Fields["SyncBlock"].Offset);
        Assert.Equal(arch.PointerSize, syncTableEntry.Fields["Object"].Offset);

        Assert.Equal(0, syncBlockType.Fields["InteropInfo"].Offset);
        Assert.Equal(arch.PointerSize, syncBlockType.Fields["Lock"].Offset);
        Assert.Equal(2 * arch.PointerSize, syncBlockType.Fields["ThinLock"].Offset);
        Assert.Equal(arch.Is64Bit ? 24 : 12, syncBlockType.Fields["LinkNext"].Offset);

        Assert.Equal(0, interopSyncBlockInfo.Fields["RCW"].Offset);
        Assert.Equal(arch.PointerSize, interopSyncBlockInfo.Fields["CCW"].Offset);
        Assert.Equal(2 * arch.PointerSize, interopSyncBlockInfo.Fields["CCF"].Offset);
    }

}
