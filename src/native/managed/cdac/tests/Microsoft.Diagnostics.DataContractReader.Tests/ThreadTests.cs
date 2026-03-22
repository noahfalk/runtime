// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DataContractReader;
using Microsoft.Diagnostics.DataContractReader.Contracts;
using Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;
using Xunit;

namespace Microsoft.Diagnostics.DataContractReader.Tests;

public sealed class ThreadTests
{
    private static IThread CreateThreadContract(
        MockTarget.Architecture arch,
        Action<MockThreadBuilder> configure)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddThread(configure)
            .Build();

        return process.CreateContractDescriptorTarget().Contracts.Thread;
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetThreadStoreData(MockTarget.Architecture arch)
    {
        const int ThreadCount = 15;
        const int UnstartedCount = 1;
        const int BackgroundCount = 2;
        const int PendingCount = 3;
        const int DeadCount = 4;

        MockThreadBuilder? threadBuilder = null;
        IThread contract = CreateThreadContract(arch, thread =>
        {
            threadBuilder = thread;
            thread.SetThreadCounts(
                ThreadCount,
                UnstartedCount,
                BackgroundCount,
                PendingCount,
                DeadCount);
        });

        Assert.NotNull(contract);
        Assert.NotNull(threadBuilder);

        ThreadStoreCounts counts = contract.GetThreadCounts();
        Assert.Equal(UnstartedCount, counts.UnstartedThreadCount);
        Assert.Equal(BackgroundCount, counts.BackgroundThreadCount);
        Assert.Equal(PendingCount, counts.PendingThreadCount);
        Assert.Equal(DeadCount, counts.DeadThreadCount);

        ThreadStoreData data = contract.GetThreadStoreData();
        Assert.Equal(ThreadCount, data.ThreadCount);
        Assert.Equal(new TargetPointer(threadBuilder.FinalizerThreadAddress), data.FinalizerThread);
        Assert.Equal(new TargetPointer(threadBuilder.GCThreadAddress), data.GCThread);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetThreadData(MockTarget.Architecture arch)
    {
        const uint Id = 1;
        TargetNUInt osId = new(1234);

        ulong threadAddress = 0;
        IThread contract = CreateThreadContract(arch, thread =>
        {
            threadAddress = thread.AddThread(Id, osId.Value);
        });

        Assert.NotNull(contract);

        ThreadData data = contract.GetThreadData(new TargetPointer(threadAddress));
        Assert.Equal(Id, data.Id);
        Assert.Equal(osId, data.OSId);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void IterateThreads(MockTarget.Architecture arch)
    {
        const uint ExpectedCount = 10;
        const uint OsIdStart = 1000;

        IThread contract = CreateThreadContract(arch, thread =>
        {
            for (uint i = 1; i <= ExpectedCount; i++)
            {
                thread.AddThread(i, i + OsIdStart);
            }
        });

        Assert.NotNull(contract);

        TargetPointer currentThread = contract.GetThreadStoreData().FirstThread;
        uint count = 0;
        while (currentThread != TargetPointer.Null)
        {
            count++;
            ThreadData threadData = contract.GetThreadData(currentThread);
            Assert.Equal(count, threadData.Id);
            Assert.Equal(count + OsIdStart, threadData.OSId.Value);
            currentThread = threadData.NextThread;
        }

        Assert.Equal(ExpectedCount, count);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetThreadAllocContext(MockTarget.Architecture arch)
    {
        const long AllocBytes = 1024;
        const long AllocBytesLoh = 4096;

        ulong threadAddress = 0;
        IThread contract = CreateThreadContract(arch, thread =>
        {
            threadAddress = thread.AddThread(1, 1234, AllocBytes, AllocBytesLoh);
        });

        Assert.NotNull(contract);

        contract.GetThreadAllocContext(new TargetPointer(threadAddress), out long resultAllocBytes, out long resultAllocBytesLoh);
        Assert.Equal(AllocBytes, resultAllocBytes);
        Assert.Equal(AllocBytesLoh, resultAllocBytesLoh);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetThreadAllocContext_ZeroValues(MockTarget.Architecture arch)
    {
        ulong threadAddress = 0;
        IThread contract = CreateThreadContract(arch, thread =>
        {
            threadAddress = thread.AddThread(1, 1234);
        });

        Assert.NotNull(contract);

        contract.GetThreadAllocContext(new TargetPointer(threadAddress), out long allocBytes, out long allocBytesLoh);
        Assert.Equal(0, allocBytes);
        Assert.Equal(0, allocBytesLoh);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetStackLimits(MockTarget.Architecture arch)
    {
        TargetPointer stackBase = new(0xAA00);
        TargetPointer stackLimit = new(0xA000);
        ulong threadAddress = 0;
        Target target = new MockProcessBuilder(arch)
            .AddThread(thread =>
            {
                threadAddress = thread.AddThread(1, 1234);
                thread.SetStackLimits(threadAddress, stackBase.Value, stackLimit.Value);
            })
            .Build()
            .CreateContractDescriptorTarget();
        IThread contract = target.Contracts.Thread;

        Assert.NotNull(contract);

        Target.TypeInfo threadType = target.GetTypeInfo(DataType.Thread);
        TargetPointer expectedFrameAddress = new(threadAddress + (ulong)threadType.Fields["Frame"].Offset);

        contract.GetStackLimitData(new TargetPointer(threadAddress), out TargetPointer outStackBase, out TargetPointer outStackLimit, out TargetPointer outFrameAddress);
        Assert.Equal(stackBase, outStackBase);
        Assert.Equal(stackLimit, outStackLimit);
        Assert.Equal(expectedFrameAddress, outFrameAddress);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCurrentExceptionHandle_NoException(MockTarget.Architecture arch)
    {
        ulong threadAddress = 0;
        IThread contract = CreateThreadContract(arch, thread =>
        {
            threadAddress = thread.AddThread(1, 1234);
        });

        Assert.NotNull(contract);

        TargetPointer thrownObjectHandle = contract.GetCurrentExceptionHandle(new TargetPointer(threadAddress));
        Assert.Equal(TargetPointer.Null, thrownObjectHandle);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCurrentExceptionHandle_WithException(MockTarget.Architecture arch)
    {
        ulong threadAddress = 0;
        ulong expectedHandleAddress = 0;
        IThread contract = CreateThreadContract(arch, thread =>
        {
            threadAddress = thread.AddThread(1, 1234);
            expectedHandleAddress = thread.SetThrownObjectHandle(threadAddress, 0xA001);
        });

        Assert.NotNull(contract);

        TargetPointer thrownObjectHandle = contract.GetCurrentExceptionHandle(new TargetPointer(threadAddress));
        Assert.Equal(new TargetPointer(expectedHandleAddress), thrownObjectHandle);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCurrentExceptionHandle_NullExceptionTracker(MockTarget.Architecture arch)
    {
        ulong threadAddress = 0;
        IThread contract = CreateThreadContract(arch, thread =>
        {
            threadAddress = thread.AddThread(1, 1234);
            thread.SetExceptionTracker(threadAddress, 0);
        });

        Assert.NotNull(contract);

        TargetPointer thrownObjectHandle = contract.GetCurrentExceptionHandle(new TargetPointer(threadAddress));
        Assert.Equal(TargetPointer.Null, thrownObjectHandle);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetCurrentExceptionHandle_HandlePointsToNull(MockTarget.Architecture arch)
    {
        ulong threadAddress = 0;
        IThread contract = CreateThreadContract(arch, thread =>
        {
            threadAddress = thread.AddThread(1, 1234);
            thread.SetThrownObjectHandle(threadAddress, 0);
        });

        Assert.NotNull(contract);

        TargetPointer thrownObjectHandle = contract.GetCurrentExceptionHandle(new TargetPointer(threadAddress));
        Assert.Equal(TargetPointer.Null, thrownObjectHandle);
    }
}
