// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

using Microsoft.Diagnostics.DataContractReader.ExecutionManagerHelpers;
using Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

namespace Microsoft.Diagnostics.DataContractReader.Tests.ExecutionManager;

public class HashMapTests
{
    private static Target CreateTarget(
        MockTarget.Architecture arch,
        Action<MockHashMapBuilder> configure)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddHashMap(configure)
            .Build();

        return process.CreateContractDescriptorTarget();
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetValue(MockTarget.Architecture arch)
    {
        (ulong Key, ulong Value)[] entries =
        [
            (0x100, 0x10),
            (0x200, 0x20),
            (0x300, 0x30),
            (0x400, 0x40),
        ];
        TargetPointer mapAddress = 0;
        TargetPointer ptrMapAddress = 0;
        Target target = CreateTarget(arch, hashMap =>
        {
            mapAddress = hashMap.CreateMap(entries);
            ptrMapAddress = hashMap.CreatePtrMap(entries);
        });

        var lookup = HashMapLookup.Create(target);
        var ptrLookup = PtrHashMapLookup.Create(target);
        foreach (var entry in entries)
        {
            TargetPointer value = lookup.GetValue(mapAddress, entry.Key);
            Assert.Equal(new TargetPointer(entry.Value), value);

            TargetPointer ptrValue = ptrLookup.GetValue(ptrMapAddress, entry.Key);
            Assert.Equal(new TargetPointer(entry.Value), ptrValue);
        }
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetValue_Collision(MockTarget.Architecture arch)
    {
        // Keys are chosen to result in a collision based on HashMapLookup.HashFunction and the size
        // of the map (based on the number of entries in the mock hash-map builder).
        // They result in the same seed and there are more entries than HashMapSlotsPerBucket
        (ulong Key, ulong Value) firstEntryDuplicateKey = (0x04, 0x40);
        (ulong Key, ulong Value)[] entries =
        [
            firstEntryDuplicateKey,
            (0x04, 0x41),
            (0x05, 0x50),
            (0x06, 0x60),
            (0x07, 0x70),
        ];
        TargetPointer mapAddress = 0;
        TargetPointer ptrMapAddress = 0;
        Target target = CreateTarget(arch, hashMap =>
        {
            mapAddress = hashMap.CreateMap(entries);
            ptrMapAddress = hashMap.CreatePtrMap(entries);
        });

        var lookup = HashMapLookup.Create(target);
        var ptrLookup = PtrHashMapLookup.Create(target);
        foreach (var entry in entries)
        {
            TargetPointer expectedValue = entry.Key == firstEntryDuplicateKey.Key ? firstEntryDuplicateKey.Value : entry.Value;
            TargetPointer value = lookup.GetValue(mapAddress, entry.Key);
            Assert.Equal(expectedValue, value);

            TargetPointer ptrValue = ptrLookup.GetValue(ptrMapAddress, entry.Key);
            Assert.Equal(expectedValue, ptrValue);
        }
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetValue_NoMatch(MockTarget.Architecture arch)
    {
        (ulong Key, ulong Value)[] entries = [(0x100, 0x010)];
        TargetPointer mapAddress = 0;
        TargetPointer ptrMapAddress = 0;
        Target target = CreateTarget(arch, hashMap =>
        {
            mapAddress = hashMap.CreateMap(entries);
            ptrMapAddress = hashMap.CreatePtrMap(entries);
        });

        {
            var lookup = HashMapLookup.Create(target);
            TargetPointer value = lookup.GetValue(mapAddress, 0x101);
            Assert.Equal((uint)HashMapLookup.SpecialKeys.InvalidEntry, value);
        }
        {
            var lookup = PtrHashMapLookup.Create(target);
            TargetPointer value = lookup.GetValue(ptrMapAddress, 0x101);
            Assert.Equal((uint)HashMapLookup.SpecialKeys.InvalidEntry, value);
        }
    }
}
