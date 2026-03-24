// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DataContractReader.Contracts;
using Moq;
using Xunit;
using Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

namespace Microsoft.Diagnostics.DataContractReader.Tests;

public class BuiltInCOMTests
{
    private const uint TestRCWInterfaceCacheSize = 8;

    private sealed class FixedContractFactory<TContract>(TContract contract) : IContractFactory<TContract>
        where TContract : IContract
    {
        public TContract CreateContract(Target target, int version) => contract;
    }

    private static IBuiltInCOM CreateBuiltInComTarget(
        MockTarget.Architecture arch,
        Action<MockBuiltInComBuilder> configure,
        ISyncBlock? syncBlock = null)
    {
        MockProcessBuilder processBuilder = new(arch);

        processBuilder
            .AddBuiltInCom(config =>
            {
                config.RCWInterfaceCacheSize = TestRCWInterfaceCacheSize;
                configure(config);
            })
            .AddSyncBlock(static _ => { });

        MockProcess process = processBuilder.Build();
        Target target = syncBlock is null
            ? process.CreateContractDescriptorTarget()
            : process.CreateContractDescriptorTarget([new FixedContractFactory<ISyncBlock>(syncBlock)]);
        return target.Contracts.BuiltInCOM;
    }

    // Flag values matching the C++ runtime
    private const ulong IsLayoutCompleteFlag = 0x10;

    // LinkedWrapperTerminator: (PTR_ComCallWrapper)-1, all bits set
    private const ulong LinkedWrapperTerminator = ulong.MaxValue;

    private const ulong ComRefcountMask = 0x000000007FFFFFFF;

    // CCWThisMask: ~0x3f on 64-bit, ~0x1f on 32-bit (matches enum_ThisMask in ComCallWrapper)
    private static ulong GetCCWThisMask(int pointerSize) => pointerSize == 8 ? ~0x3FUL : ~0x1FUL;

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_RefCount_ReturnsMaskedValue(MockTarget.Architecture arch)
    {
        // Raw refcount has CLEANUP_SENTINEL (bit 31) set plus a visible count of 0x1234_5678.
        ulong rawRefCount = 0x0000_0000_1234_5678UL | 0x80000000UL;
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.RefCount = rawRefCount;
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        // RefCount should have the CLEANUP_SENTINEL bit stripped by the contract.
        Assert.Equal(rawRefCount & ComRefcountMask, data.RefCount);
        Assert.True(data.IsNeutered);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_IsHandleWeak_FlagSet_ReturnsTrue(MockTarget.Architecture arch)
    {
        const uint IsHandleWeakFlag = 0x4;
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.Flags = IsHandleWeakFlag;
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.True(data.IsHandleWeak);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_IsHandleWeak_FlagNotSet_ReturnsFalse(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.False(data.IsHandleWeak);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_IsNeutered_SentinelBitSet_ReturnsTrue(MockTarget.Architecture arch)
    {
        ulong rawRefCount = 0x80000000UL; // CLEANUP_SENTINEL bit set
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.RefCount = rawRefCount;
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.True(data.IsNeutered);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_IsNeutered_SentinelBitClear_ReturnsFalse(MockTarget.Architecture arch)
    {
        ulong rawRefCount = 3UL; // non-zero ref count, no sentinel
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.RefCount = rawRefCount;
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.False(data.IsNeutered);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_IsAggregated_FlagSet_ReturnsTrue(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.Flags = 0x1; // IsAggregated flag
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.True(data.IsAggregated);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_IsAggregated_FlagNotSet_ReturnsFalse(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.False(data.IsAggregated);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_IsExtendsCOMObject_FlagSet_ReturnsTrue(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.Flags = 0x2; // IsExtendsCom flag
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.True(data.IsExtendsCOMObject);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_IsExtendsCOMObject_FlagNotSet_ReturnsFalse(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = builtInCOM.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.False(data.IsExtendsCOMObject);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCCWInterfaces_SingleWrapper_SkipsNullAndIncompleteSlots(MockTarget.Architecture arch)
    {
        int P = arch.PointerSize;
        const ulong ExpectedMethodTable2 = 0xdead_0002;

        MockComCallWrapper? wrapper = null;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch, builtInCom =>
        {
            MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
            wrapper = builtInCom.AddComCallWrapper();
            simpleWrapper.MainWrapper = wrapper.Address;
            wrapper.SimpleWrapper = simpleWrapper.Address;
            wrapper.Next = LinkedWrapperTerminator;
            // Slot 0: IUnknown (layout complete; MethodTable is Null per spec for first wrapper's slot 0)
            MockComMethodTable cmt0 = builtInCom.AddComMethodTable();

            // Slot 1: incomplete layout (should be skipped)
            MockComMethodTable cmt1 = builtInCom.AddComMethodTable();

            // Slot 2: layout complete with a MethodTable
            MockComMethodTable cmt2 = builtInCom.AddComMethodTable();

            wrapper.InterfacePointers[0] = cmt0.VTable;
            wrapper.InterfacePointers[1] = cmt1.VTable;
            wrapper.InterfacePointers[2] = cmt2.VTable;
            wrapper.InterfacePointers[3] = 0;
            wrapper.InterfacePointers[4] = 0;
            wrapper.Next = LinkedWrapperTerminator;

            cmt0.Flags = IsLayoutCompleteFlag;
            cmt0.MethodTable = 0;

            cmt1.Flags = 0;

            cmt2.Flags = IsLayoutCompleteFlag;
            cmt2.MethodTable = ExpectedMethodTable2;
        });

        List<COMInterfacePointerData> interfaces =
            contract.GetCCWInterfaces(new TargetPointer(wrapper.Address)).ToList();

        // Only slot 0 and slot 2 appear: slot 1 is incomplete, slots 3/4 are null
        Assert.Equal(2, interfaces.Count);

        // Slot 0: IUnknown (first wrapper, index 0) => MethodTable = Null
        Assert.Equal(wrapper.InterfacePointerAddress, interfaces[0].InterfacePointerAddress.Value);
        Assert.Equal(TargetPointer.Null.Value, interfaces[0].MethodTable.Value);

        // Slot 2: at offset 3*P from CCW base (IPtr + 2*P)
        Assert.Equal(wrapper.InterfacePointerAddress + (ulong)(2 * P), interfaces[1].InterfacePointerAddress.Value);
        Assert.Equal(ExpectedMethodTable2, interfaces[1].MethodTable.Value);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCCWInterfaces_MultipleWrappers_WalksChain(MockTarget.Architecture arch)
    {
        int P = arch.PointerSize;
        const ulong ExpectedMethodTableSlot0 = 0xbbbb_0000;
        const ulong ExpectedMethodTableSlot2 = 0xcccc_0002;

        MockComCallWrapper? wrapper1 = null;
        MockComCallWrapper? wrapper2 = null;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch, builtInCom =>
        {
            MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
            wrapper1 = builtInCom.AddComCallWrapper();
            wrapper2 = builtInCom.AddComCallWrapper();
            simpleWrapper.MainWrapper = wrapper1.Address;
            wrapper1.SimpleWrapper = simpleWrapper.Address;
            wrapper1.Next = wrapper2.Address;
            wrapper2.SimpleWrapper = simpleWrapper.Address;
            wrapper2.Next = LinkedWrapperTerminator;
            MockComMethodTable cmt1_0 = builtInCom.AddComMethodTable();

            MockComMethodTable cmt2_0 = builtInCom.AddComMethodTable();

            MockComMethodTable cmt2_2 = builtInCom.AddComMethodTable();

            wrapper1.InterfacePointers[0] = cmt1_0.VTable;
            wrapper1.InterfacePointers[1] = 0;
            wrapper1.InterfacePointers[2] = 0;
            wrapper1.InterfacePointers[3] = 0;
            wrapper1.InterfacePointers[4] = 0;

            wrapper2.InterfacePointers[0] = cmt2_0.VTable;
            wrapper2.InterfacePointers[1] = 0;
            wrapper2.InterfacePointers[2] = cmt2_2.VTable;
            wrapper2.InterfacePointers[3] = 0;
            wrapper2.InterfacePointers[4] = 0;

            cmt1_0.Flags = IsLayoutCompleteFlag;
            cmt1_0.MethodTable = 0;

            cmt2_0.Flags = IsLayoutCompleteFlag;
            cmt2_0.MethodTable = ExpectedMethodTableSlot0;

            cmt2_2.Flags = IsLayoutCompleteFlag;
            cmt2_2.MethodTable = ExpectedMethodTableSlot2;
        });

        List<COMInterfacePointerData> interfaces =
            contract.GetCCWInterfaces(new TargetPointer(wrapper1.Address)).ToList();

        // 3 interfaces: ccw1 slot0 (IUnknown), ccw2 slot0 (IClassX), ccw2 slot2 (interface)
        Assert.Equal(3, interfaces.Count);

        // First wrapper, slot 0: IUnknown => MethodTable = Null
        Assert.Equal(wrapper1.InterfacePointerAddress, interfaces[0].InterfacePointerAddress.Value);
        Assert.Equal(TargetPointer.Null.Value, interfaces[0].MethodTable.Value);

        // Second wrapper, slot 0: IClassX - has a MethodTable (not first wrapper)
        Assert.Equal(wrapper2.InterfacePointerAddress, interfaces[1].InterfacePointerAddress.Value);
        Assert.Equal(ExpectedMethodTableSlot0, interfaces[1].MethodTable.Value);

        // Second wrapper, slot 2
        Assert.Equal(wrapper2.InterfacePointerAddress + (ulong)(2 * P), interfaces[2].InterfacePointerAddress.Value);
        Assert.Equal(ExpectedMethodTableSlot2, interfaces[2].MethodTable.Value);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCCWInterfaces_LinkedWrapper_WalksFullChainFromAnyWrapper(MockTarget.Architecture arch)
    {
        int P = arch.PointerSize;
        const ulong ExpectedMethodTable = 0xaaaa_0001;

        MockComCallWrapper? wrapper1 = null;
        MockComCallWrapper? wrapper2 = null;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch, builtInCom =>
        {
            MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
            wrapper1 = builtInCom.AddComCallWrapper();
            wrapper2 = builtInCom.AddComCallWrapper();
            simpleWrapper.MainWrapper = wrapper1.Address;
            wrapper1.SimpleWrapper = simpleWrapper.Address;
            wrapper1.Next = wrapper2.Address;
            wrapper2.SimpleWrapper = simpleWrapper.Address;
            wrapper2.Next = LinkedWrapperTerminator;
            MockComMethodTable cmt1 = builtInCom.AddComMethodTable();

            MockComMethodTable cmt2 = builtInCom.AddComMethodTable();

            wrapper1.InterfacePointers[0] = cmt1.VTable;
            wrapper1.InterfacePointers[1] = 0;
            wrapper1.InterfacePointers[2] = 0;
            wrapper1.InterfacePointers[3] = 0;
            wrapper1.InterfacePointers[4] = 0;

            wrapper2.InterfacePointers[0] = 0;
            wrapper2.InterfacePointers[1] = cmt2.VTable;
            wrapper2.InterfacePointers[2] = 0;
            wrapper2.InterfacePointers[3] = 0;
            wrapper2.InterfacePointers[4] = 0;

            cmt1.Flags = IsLayoutCompleteFlag;
            cmt1.MethodTable = 0;

            cmt2.Flags = IsLayoutCompleteFlag;
            cmt2.MethodTable = ExpectedMethodTable;
        });

        // Passing the start CCW enumerates both wrappers' interfaces.
        List<COMInterfacePointerData> interfacesFromStart =
            contract.GetCCWInterfaces(new TargetPointer(wrapper1.Address)).ToList();

        Assert.Equal(2, interfacesFromStart.Count);
        // ccw1 slot 0: IUnknown → MethodTable = Null (first wrapper, slot 0)
        Assert.Equal(wrapper1.InterfacePointerAddress, interfacesFromStart[0].InterfacePointerAddress.Value);
        Assert.Equal(TargetPointer.Null.Value, interfacesFromStart[0].MethodTable.Value);
        // ccw2 slot 1
        Assert.Equal(wrapper2.InterfacePointerAddress + (ulong)P, interfacesFromStart[1].InterfacePointerAddress.Value);
        Assert.Equal(ExpectedMethodTable, interfacesFromStart[1].MethodTable.Value);

        // Passing the second (non-start) CCW also navigates to the start and enumerates the full chain.
        List<COMInterfacePointerData> interfacesFromLinked =
            contract.GetCCWInterfaces(new TargetPointer(wrapper2.Address)).ToList();

        Assert.Equal(interfacesFromStart.Count, interfacesFromLinked.Count);
        for (int i = 0; i < interfacesFromStart.Count; i++)
        {
            Assert.Equal(interfacesFromStart[i].InterfacePointerAddress.Value, interfacesFromLinked[i].InterfacePointerAddress.Value);
            Assert.Equal(interfacesFromStart[i].MethodTable.Value, interfacesFromLinked[i].MethodTable.Value);
        }
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWInterfaces_ReturnsFilledEntries(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;
        (TargetPointer MethodTable, TargetPointer Unknown)[] expectedEntries =
        [
            (new TargetPointer(0x1000), new TargetPointer(0x2000)),
            (new TargetPointer(0x3000), new TargetPointer(0x4000)),
        ];

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddRCWWithInlineEntries(
                    expectedEntries.Select(entry => ((ulong)entry.MethodTable, (ulong)entry.Unknown)).ToArray()));
            });

        Assert.NotNull(contract);

        List<(TargetPointer MethodTable, TargetPointer Unknown)> results =
            contract.GetRCWInterfaces(rcwAddress).ToList();

        Assert.Equal(expectedEntries.Length, results.Count);
        for (int i = 0; i < expectedEntries.Length; i++)
        {
            Assert.Equal(expectedEntries[i].MethodTable, results[i].MethodTable);
            Assert.Equal(expectedEntries[i].Unknown, results[i].Unknown);
        }
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCCWInterfaces_ComIpAddress_ResolvesToCCW(MockTarget.Architecture arch)
    {
        int P = arch.PointerSize;

        // Place the CCW at a CCWThisMask-aligned address so that (ccwAddr + P) & thisMask == ccwAddr.
        // On 64-bit: alignment = 64 bytes; on 32-bit: alignment = 32 bytes.
        ulong thisMask = GetCCWThisMask(P);
        ulong alignment = ~thisMask + 1; // = 64 on 64-bit, 32 on 32-bit
        ulong tearOffAddRefAddress = 0;
        MockComCallWrapper? wrapper = null;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch, builtInCom =>
        {
            MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
            wrapper = builtInCom.AddComCallWrapper();
            tearOffAddRefAddress = builtInCom.TearOffAddRefAddress;
            simpleWrapper.MainWrapper = wrapper.Address;
            wrapper.SimpleWrapper = simpleWrapper.Address;
            wrapper.Next = LinkedWrapperTerminator;
            MockComMethodTable cmt = builtInCom.AddComMethodTable(vtableSlots: 2);
            wrapper.InterfacePointers[0] = cmt.VTable;
            wrapper.InterfacePointers[1] = 0;
            wrapper.InterfacePointers[2] = 0;
            wrapper.InterfacePointers[3] = 0;
            wrapper.InterfacePointers[4] = 0;

            cmt.Flags = IsLayoutCompleteFlag;
            cmt.MethodTable = 0;
            cmt.SetVTableSlot(0, 0);
            cmt.SetVTableSlot(1, tearOffAddRefAddress);
        });

        Assert.Equal(0UL, wrapper.Address % alignment);

        // COM IP = alignedCCWAddr + P (= address of IP[0] slot within the CCW).
        // *comIP = vtable; vtable[1] = TearOffAddRefAddr → detected as standard CCW IP.
        // (alignedCCWAddr + P) & thisMask = alignedCCWAddr (since P < alignment).
        ulong comIPAddr = wrapper.InterfacePointerAddress;

        // GetCCWFromInterfacePointer resolves the COM IP to the start CCW pointer.
        TargetPointer startCCWFromIP = contract.GetCCWFromInterfacePointer(new TargetPointer(comIPAddr));
        Assert.Equal(wrapper.Address, startCCWFromIP.Value);

        // A direct CCW pointer is not a COM IP; GetCCWFromInterfacePointer returns Null.
        TargetPointer nullResult = contract.GetCCWFromInterfacePointer(new TargetPointer(wrapper.Address));
        Assert.Equal(TargetPointer.Null, nullResult);

        // GetCCWInterfaces works with either the resolved IP or the direct CCW pointer.
        List<COMInterfacePointerData> ifacesDirect =
            contract.GetCCWInterfaces(new TargetPointer(wrapper.Address)).ToList();
        List<COMInterfacePointerData> ifacesFromIP =
            contract.GetCCWInterfaces(startCCWFromIP).ToList();

        // Both paths should produce the same interfaces
        Assert.Equal(ifacesDirect.Count, ifacesFromIP.Count);
        for (int i = 0; i < ifacesDirect.Count; i++)
        {
            Assert.Equal(ifacesDirect[i].InterfacePointerAddress.Value, ifacesFromIP[i].InterfacePointerAddress.Value);
            Assert.Equal(ifacesDirect[i].MethodTable.Value, ifacesFromIP[i].MethodTable.Value);
        }

    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWInterfaces_SkipsEntriesWithNullUnknown(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;
        // The IsFree() check uses only Unknown == null; entries with Unknown == null are skipped.
        (TargetPointer MethodTable, TargetPointer Unknown)[] entries =
        [
            (new TargetPointer(0x1000), new TargetPointer(0x2000)),
            (TargetPointer.Null, TargetPointer.Null),  // free entry (Unknown == null)
            (new TargetPointer(0x5000), new TargetPointer(0x6000)),
        ];

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddRCWWithInlineEntries(
                    entries.Select(entry => ((ulong)entry.MethodTable, (ulong)entry.Unknown)).ToArray()));
            });

        List<(TargetPointer MethodTable, TargetPointer Unknown)> results =
            contract.GetRCWInterfaces(rcwAddress).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(new TargetPointer(0x1000), results[0].MethodTable);
        Assert.Equal(new TargetPointer(0x2000), results[0].Unknown);
        Assert.Equal(new TargetPointer(0x5000), results[1].MethodTable);
        Assert.Equal(new TargetPointer(0x6000), results[1].Unknown);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWInterfaces_EmptyCache_ReturnsEmpty(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddRCWWithInlineEntries([]));
            });

        List<(TargetPointer MethodTable, TargetPointer Unknown)> results =
            contract.GetRCWInterfaces(rcwAddress).ToList();

        Assert.Empty(results);
    }

    // Bit-flag constants mirroring BuiltInCOM_1 internal constants, used to construct Flags for GetRCWData tests.
    private const uint RCWFlagAggregated   = 0x10u;   // URTAggregatedMask
    private const uint RCWFlagContained    = 0x20u;   // URTContainedMask
    private const uint RCWFlagFreeThreaded = 0x100u;  // MarshalingTypeFreeThreadedValue

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWData_ReturnsScalarFields(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;
        TargetPointer expectedIdentity  = new TargetPointer(0x1000_0000);
        TargetPointer expectedVTable    = new TargetPointer(0x2000_0000);
        TargetPointer expectedThread    = new TargetPointer(0x3000_0000);
        TargetPointer expectedCookie    = new TargetPointer(0x4000_0000);
        uint          expectedRefCount  = 42;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddFullRCW(
                    identityPointer: (ulong)expectedIdentity,
                    vtablePtr: (ulong)expectedVTable,
                    creatorThread: (ulong)expectedThread,
                    ctxCookie: (ulong)expectedCookie,
                    refCount: expectedRefCount));
            });

        RCWData result = contract.GetRCWData(rcwAddress);

        Assert.Equal(expectedIdentity, result.IdentityPointer);
        Assert.Equal(expectedVTable, result.VTablePtr);
        Assert.Equal(expectedThread, result.CreatorThread);
        Assert.Equal(expectedCookie, result.CtxCookie);
        Assert.Equal(expectedRefCount, result.RefCount);
        Assert.Equal(TargetPointer.Null, result.ManagedObject);
        Assert.False(result.IsAggregated);
        Assert.False(result.IsContained);
        Assert.False(result.IsFreeThreaded);
        Assert.False(result.IsDisconnected);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWData_FlagsAggregatedAndContained(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddFullRCW(
                    flags: RCWFlagAggregated | RCWFlagContained));
            });

        RCWData result = contract.GetRCWData(rcwAddress);

        Assert.True(result.IsAggregated);
        Assert.True(result.IsContained);
        Assert.False(result.IsFreeThreaded);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWData_FlagsFreeThreaded(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddFullRCW(
                    flags: RCWFlagFreeThreaded));
            });

        RCWData result = contract.GetRCWData(rcwAddress);

        Assert.True(result.IsFreeThreaded);
        Assert.False(result.IsAggregated);
        Assert.False(result.IsContained);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWData_IsDisconnected_Sentinel(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;
        const ulong DisconnectedSentinel = 0xBADF00D;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddFullRCW(
                    unknownPointer: DisconnectedSentinel));
            });

        RCWData result = contract.GetRCWData(rcwAddress);

        Assert.True(result.IsDisconnected);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWData_IsDisconnected_CtxCookieMismatch(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                // Allocate a CtxEntry whose CtxCookie differs from the RCW's CtxCookie.
                TargetPointer ctxCookieInEntry = new TargetPointer(0xAAAA_0000);
                ulong ctxEntryAddress = builtInCom.AddCtxEntry(ctxCookie: (ulong)ctxCookieInEntry);

                TargetPointer ctxCookieInRcw = new TargetPointer(0xBBBB_0000);  // different from entry
                rcwAddress = new TargetPointer(builtInCom.AddFullRCW(
                    ctxCookie: (ulong)ctxCookieInRcw,
                    ctxEntry: ctxEntryAddress));  // bit 0 clear → not null, not adjusted
            });

        RCWData result = contract.GetRCWData(rcwAddress);

        Assert.True(result.IsDisconnected);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWData_ManagedObject_ResolvedViaSyncBlockIndex(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;
        TargetPointer expectedManagedObject = new TargetPointer(0xDEAD_BEEF_0000UL);
        const uint syncBlockIndex = 3;

        var mockSyncBlock = new Mock<ISyncBlock>();
        mockSyncBlock.Setup(s => s.GetSyncBlockObject(syncBlockIndex)).Returns(expectedManagedObject);

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddFullRCW(
                    syncBlockIndex: syncBlockIndex));
            },
            syncBlock: mockSyncBlock.Object);

        RCWData result = contract.GetRCWData(rcwAddress);

        Assert.Equal(expectedManagedObject, result.ManagedObject);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetRCWContext_ReturnsCtxCookie(MockTarget.Architecture arch)
    {
        TargetPointer rcwAddress = default;
        TargetPointer expectedCookie = new TargetPointer(0xC00C_1E00);

        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                rcwAddress = new TargetPointer(builtInCom.AddRCWWithInlineEntries([], (ulong)expectedCookie));
            });

        TargetPointer result = contract.GetRCWContext(rcwAddress);

        Assert.Equal(expectedCookie, result);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCCWFromInterfacePointer_SCCWIp_ResolvesToStartCCW(MockTarget.Architecture arch)
    {
        int P = arch.PointerSize;
        const int InterfaceKind = 1;
        MockComCallWrapper? wrapper = null;
        MockSimpleComCallWrapper? simpleWrapper = null;
        ulong tearOffAddRefSimpleAddress = 0;

        // SimpleComCallWrapper:
        //   Offset  0: RefCount (8 bytes)
        //   Offset  8: Flags (4 bytes)
        //   Offset 12: MainWrapper (pointer)
        //   Offset 12+P: VTablePtr array (at least two pointer-sized slots: kinds 0 and 1)
        IBuiltInCOM contract = CreateBuiltInComTarget(arch, builtInCom =>
        {
            simpleWrapper = builtInCom.AddSimpleComCallWrapper();
            wrapper = builtInCom.AddComCallWrapper();
            tearOffAddRefSimpleAddress = builtInCom.TearOffAddRefSimpleAddress;
            simpleWrapper.MainWrapper = wrapper.Address;
            wrapper.SimpleWrapper = simpleWrapper.Address;
            wrapper.Next = LinkedWrapperTerminator;
            MockMemorySpace.HeapFragment vtableDataFrag = builtInCom.AllocateFragment((ulong)(3 * P), "VtableData");
            ulong vtableAddr = vtableDataFrag.Address + (ulong)P;

            simpleWrapper.VTablePointers[0] = 0;
            simpleWrapper.VTablePointers[1] = vtableAddr;

            SpanWriter writer = new(arch, vtableDataFrag.Data);
            writer.Write(InterfaceKind);
            writer = new(arch, vtableDataFrag.Data.AsSpan(P, P));
            writer.WritePointer(0);
            writer = new(arch, vtableDataFrag.Data.AsSpan(2 * P, P));
            writer.WritePointer(tearOffAddRefSimpleAddress);
        });

        // SCCW IP for interfaceKind=1 is the address of the vtable pointer slot in the SCCW.
        // Reading *sccwIP gives vtableAddr; reading *(vtableAddr + P) gives TearOffAddRefSimple.
        ulong sccwIP = simpleWrapper.VTablePointerAddress + (ulong)(InterfaceKind * P);

        TargetPointer startCCW = contract.GetCCWFromInterfacePointer(new TargetPointer(sccwIP));
        Assert.Equal(wrapper.Address, startCCW.Value);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetObjectHandle_ReturnsHandleFromWrapper(MockTarget.Architecture arch)
    {
        MockMemorySpace.HeapFragment? handle = null;
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM contract = CreateBuiltInComTarget(arch, builtInCom =>
        {
            handle = builtInCom.AllocateFragment((ulong)arch.PointerSize, "Handle");
            MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
            wrapper = builtInCom.AddComCallWrapper();
            simpleWrapper.MainWrapper = wrapper.Address;
            wrapper.SimpleWrapper = simpleWrapper.Address;
            wrapper.Next = LinkedWrapperTerminator;
            wrapper.Handle = handle.Address;
        });
        TargetPointer objectHandle = contract.GetObjectHandle(new TargetPointer(wrapper.Address));
        Assert.Equal(handle.Address, objectHandle.Value);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetObjectHandle_NullHandle_ReturnsNull(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        TargetPointer handle = contract.GetObjectHandle(new TargetPointer(wrapper.Address));
        Assert.Equal(TargetPointer.Null, handle);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetStartWrapper_SingleWrapper_ReturnsSelf(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
            });
        Assert.Equal(wrapper.Address, contract.GetStartWrapper(new TargetPointer(wrapper.Address)).Value);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetStartWrapper_LinkedWrapper_NavigatesToStart(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper1 = null;
        MockComCallWrapper? wrapper2 = null;
        IBuiltInCOM builtInCOM = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper1 = builtInCom.AddComCallWrapper();
                wrapper2 = builtInCom.AddComCallWrapper();
                simpleWrapper.MainWrapper = wrapper1.Address;
                wrapper1.SimpleWrapper = simpleWrapper.Address;
                wrapper1.Next = wrapper2.Address;
                wrapper2.SimpleWrapper = simpleWrapper.Address;
                wrapper2.Next = LinkedWrapperTerminator;
            });

        // From start wrapper, returns itself
        Assert.Equal(wrapper1.Address, builtInCOM.GetStartWrapper(new TargetPointer(wrapper1.Address)).Value);
        // From linked wrapper, navigates to start
        Assert.Equal(wrapper1.Address, builtInCOM.GetStartWrapper(new TargetPointer(wrapper2.Address)).Value);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_ReturnsAllFields(MockTarget.Architecture arch)
    {
        ulong outerIUnknownAddr = 0xBBBB_0000;
        ulong rawRefCount = 0x0000_0003UL;
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.RefCount = rawRefCount;
                simpleWrapper.Flags = 0x2;
                simpleWrapper.MainWrapper = wrapper.Address;
                simpleWrapper.OuterIUnknown = outerIUnknownAddr;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = contract.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));
        Assert.Equal(rawRefCount & ComRefcountMask, data.RefCount);
        Assert.False(data.IsNeutered);
        Assert.False(data.IsAggregated);
        Assert.True(data.IsExtendsCOMObject);
        Assert.False(data.IsHandleWeak);
        Assert.Equal(outerIUnknownAddr, data.OuterIUnknown.Value);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSimpleComCallWrapperData_ZeroFields_AreNull(MockTarget.Architecture arch)
    {
        MockComCallWrapper? wrapper = null;
        IBuiltInCOM contract = CreateBuiltInComTarget(arch,
            builtInCom =>
            {
                MockSimpleComCallWrapper simpleWrapper = builtInCom.AddSimpleComCallWrapper();
                wrapper = builtInCom.AddComCallWrapper();
                simpleWrapper.MainWrapper = wrapper.Address;
                wrapper.SimpleWrapper = simpleWrapper.Address;
                wrapper.Next = LinkedWrapperTerminator;
            });
        SimpleComCallWrapperData data = contract.GetSimpleComCallWrapperData(new TargetPointer(wrapper.Address));

        Assert.Equal(0UL, data.RefCount);
        Assert.False(data.IsNeutered);
        Assert.False(data.IsAggregated);
        Assert.False(data.IsExtendsCOMObject);
        Assert.False(data.IsHandleWeak);
        Assert.Equal(TargetPointer.Null, data.OuterIUnknown);
    }
}

