// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;
using Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

namespace Microsoft.Diagnostics.DataContractReader.Tests;

public class SyncBlockTests
{
    private static Target CreateTarget(
        MockTarget.Architecture arch,
        Action<MockSyncBlockBuilder> configure)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddSyncBlock(configure)
            .Build();

        return process.CreateContractDescriptorTarget();
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSyncBlockFromCleanupList_SingleItem(MockTarget.Architecture arch)
    {
        ulong syncBlockAddr = 0;
        Target target = CreateTarget(arch, config =>
        {
            syncBlockAddr = config.AddSyncBlockToCleanupList(TargetPointer.Null, TargetPointer.Null, TargetPointer.Null);
        });

        TargetPointer result = target.Contracts.SyncBlock.GetSyncBlockFromCleanupList();

        Assert.Equal(new TargetPointer(syncBlockAddr), result);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetSyncBlockFromCleanupList_MultipleItems_ReturnsFirst(MockTarget.Architecture arch)
    {
        ulong addedSecond = 0;
        // Items are prepended, so addedSecond is at the head
        Target target = CreateTarget(arch, config =>
        {
            config.AddSyncBlockToCleanupList(TargetPointer.Null, TargetPointer.Null, TargetPointer.Null);
            addedSecond = config.AddSyncBlockToCleanupList(TargetPointer.Null, TargetPointer.Null, TargetPointer.Null);
        });

        TargetPointer result = target.Contracts.SyncBlock.GetSyncBlockFromCleanupList();

        Assert.Equal(new TargetPointer(addedSecond), result);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetNextSyncBlock_ReturnsNextInChain(MockTarget.Architecture arch)
    {
        // Add two blocks; the second is prepended (becomes the head)
        ulong firstAdded = 0;
        ulong secondAdded = 0;
        Target target = CreateTarget(arch, config =>
        {
            firstAdded = config.AddSyncBlockToCleanupList(TargetPointer.Null, TargetPointer.Null, TargetPointer.Null);
            secondAdded = config.AddSyncBlockToCleanupList(TargetPointer.Null, TargetPointer.Null, TargetPointer.Null);
        });

        // Head of list is secondAdded; its next should be firstAdded
        TargetPointer next = target.Contracts.SyncBlock.GetNextSyncBlock(new TargetPointer(secondAdded));

        Assert.Equal(new TargetPointer(firstAdded), next);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetNextSyncBlock_LastItemReturnsNull(MockTarget.Architecture arch)
    {
        ulong syncBlockAddr = 0;
        Target target = CreateTarget(arch, config =>
        {
            syncBlockAddr = config.AddSyncBlockToCleanupList(TargetPointer.Null, TargetPointer.Null, TargetPointer.Null);
        });

        TargetPointer next = target.Contracts.SyncBlock.GetNextSyncBlock(new TargetPointer(syncBlockAddr));

        Assert.Equal(TargetPointer.Null, next);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetBuiltInComData_NoInteropInfo(MockTarget.Architecture arch)
    {
        ulong syncBlockAddr = 0;
        Target target = CreateTarget(arch, config =>
        {
            syncBlockAddr = config.AddSyncBlockToCleanupList(
                TargetPointer.Null,
                TargetPointer.Null,
                TargetPointer.Null,
                hasInteropInfo: false);
        });

        bool result = target.Contracts.SyncBlock.GetBuiltInComData(new TargetPointer(syncBlockAddr), out TargetPointer rcw, out TargetPointer ccw, out TargetPointer ccf);

        Assert.False(result);
        Assert.Equal(TargetPointer.Null, rcw);
        Assert.Equal(TargetPointer.Null, ccw);
        Assert.Equal(TargetPointer.Null, ccf);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetBuiltInComData_WithInteropData(MockTarget.Architecture arch)
    {
        TargetPointer expectedRCW = new TargetPointer(0x1000);
        TargetPointer expectedCCW = new TargetPointer(0x2000);
        TargetPointer expectedCCF = new TargetPointer(0x3000);
        ulong syncBlockAddr = 0;
        Target target = CreateTarget(arch, config =>
        {
            syncBlockAddr = config.AddSyncBlockToCleanupList(expectedRCW, expectedCCW, expectedCCF);
        });

        bool result = target.Contracts.SyncBlock.GetBuiltInComData(new TargetPointer(syncBlockAddr), out TargetPointer rcw, out TargetPointer ccw, out TargetPointer ccf);

        Assert.True(result);
        Assert.Equal(expectedRCW, rcw);
        Assert.Equal(expectedCCW, ccw);
        Assert.Equal(expectedCCF, ccf);
    }
}
