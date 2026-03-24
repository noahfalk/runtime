// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public sealed class MockHashMap : TypedView
{
    public static Layout<MockHashMap> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("HashMap", architecture)
            .AddPointerField("Buckets")
            .Build<MockHashMap>();

    public ulong Buckets
    {
        get => ReadPointerField("Buckets");
        set => WritePointerField("Buckets", value);
    }
}

public sealed class MockHashMapBucket : TypedView
{
    internal const int SlotsPerBucket = 4;

    public static Layout<MockHashMapBucket> CreateLayout(MockTarget.Architecture architecture)
    {
        int slotsSize = checked(SlotsPerBucket * architecture.PointerSize);
        return new SequentialLayoutBuilder("Bucket", architecture)
            .AddField("Keys", slotsSize)
            .AddField("Values", slotsSize)
            .Build<MockHashMapBucket>();
    }

    public int SlotCount => SlotsPerBucket;

    public ulong GetKey(int slot)
        => ReadPointerSlot("Keys", slot);

    public void SetKey(int slot, ulong value)
        => WritePointerSlot("Keys", slot, value);

    public ulong GetValue(int slot)
        => ReadPointerSlot("Values", slot);

    public void SetValue(int slot, ulong value)
        => WritePointerSlot("Values", slot, value);

    private ulong ReadPointerSlot(string fieldName, int slot)
        => ReadPointer(GetPointerSlotSlice(fieldName, slot));

    private void WritePointerSlot(string fieldName, int slot, ulong value)
        => WritePointer(GetPointerSlotSlice(fieldName, slot), value);

    private Span<byte> GetPointerSlotSlice(string fieldName, int slot)
    {
        LayoutField field = Layout.GetField(fieldName);
        int slotCount = field.Size / Architecture.PointerSize;
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, slotCount);

        return GetFieldSlice(fieldName).Slice(slot * Architecture.PointerSize, Architecture.PointerSize);
    }
}

public sealed class MockHashMapBucketArray : TypedArrayView<MockHashMapBucket>
{
    public uint BucketCount
    {
        get => ElementCount > 0 ? ReadUInt32Field(0, "Keys") : 0;
        set => WriteUInt32Field(0, "Keys", value);
    }

    public MockHashMapBucket GetBucket(int bucketIndex)
        => GetElement(bucketIndex);
}

public sealed class MockHashMapBuilder
{
    private static readonly uint[] PossibleSizes = [5, 11, 17, 23, 29, 37];

    private readonly MockMemorySpace.Builder _builder;
    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly MockTarget.Architecture _architecture;
    private readonly ulong _valueMask;

    public MockHashMapBuilder(MockMemorySpace.Builder builder)
        : this(
            builder,
            builder.DefaultAllocator)
    {
    }

    internal MockHashMapBuilder(
        MockMemorySpace.Builder builder,
        MockMemorySpace.BumpAllocator allocator)
    {
        _builder = builder;
        _allocator = allocator;
        _architecture = builder.TargetTestHelpers.Arch;
        HashMapLayout = MockHashMap.CreateLayout(_architecture);
        BucketLayout = MockHashMapBucket.CreateLayout(_architecture);
        _valueMask = GetValueMask(_architecture);
    }

    public Layout<MockHashMap> HashMapLayout { get; }

    public Layout<MockHashMapBucket> BucketLayout { get; }

    public ulong CreateMap((ulong Key, ulong Value)[] entries)
    {
        MockHashMap map = CreateHashMap();
        MockHashMapBucketArray buckets = CreateBucketArray(entries);
        map.Buckets = buckets.Address;
        return map.Address;
    }

    public MockHashMap CreateHashMap(string? name = null)
        => HashMapLayout.Allocate(_allocator, name);

    public MockHashMap GetHashMap(ulong mapAddress, string? name = null)
    {
        if (!TryGetContainingFragment(mapAddress, HashMapLayout.Size, out MockMemorySpace.HeapFragment? fragment, out int offset))
        {
            throw new InvalidOperationException($"No fragment includes addresses from 0x{mapAddress:x} with length {HashMapLayout.Size}");
        }

        Memory<byte> memory = fragment.Data.AsMemory(offset, HashMapLayout.Size);
        return HashMapLayout.Create(memory, mapAddress);
    }

    public MockHashMapBucketArray CreateBucketArray((ulong Key, ulong Value)[] entries)
    {
        uint size = GetBucketCount(entries.Length);
        uint bucketSize = checked((uint)BucketLayout.Size);
        uint numBuckets = size + 1;
        MockMemorySpace.HeapFragment fragment = AllocateFragment(bucketSize * numBuckets, $"Buckets[{numBuckets}]");
        MockHashMapBucketArray buckets = BucketLayout.CreateCustomArray<MockHashMapBucketArray>(fragment);

        buckets.BucketCount = size;

        const int MaxRetry = 8;
        foreach ((ulong key, ulong value) in entries)
        {
            HashFunction(key, size, out uint seed, out uint increment);

            int tryCount = 0;
            while (tryCount < MaxRetry)
            {
                MockHashMapBucket bucket = buckets.GetBucket((int)((seed % size) + 1));
                if (TryAddEntryToBucket(bucket, key, value))
                {
                    break;
                }

                seed += increment;
                tryCount++;
            }

            if (tryCount >= MaxRetry)
            {
                throw new InvalidOperationException("HashMap test helper does not handle re-hashing");
            }
        }

        return buckets;
    }

    public ulong CreatePtrMap((ulong Key, ulong Value)[] entries)
    {
        MockHashMap map = CreateHashMap();
        PopulatePtrMap(map, entries);
        return map.Address;
    }

    public void PopulatePtrMap(MockHashMap map, (ulong Key, ulong Value)[] entries)
    {
        (ulong Key, ulong Value)[] ptrMapEntries = new (ulong Key, ulong Value)[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            ptrMapEntries[i] = (entries[i].Key, entries[i].Value >> 1);
        }

        MockHashMapBucketArray buckets = CreateBucketArray(ptrMapEntries);
        map.Buckets = buckets.Address;
    }

    internal static ulong GetValueMask(MockTarget.Architecture architecture)
        => architecture.Is64Bit ? unchecked((ulong)long.MaxValue) : unchecked((ulong)int.MaxValue);

    private static uint GetBucketCount(int entryCount)
    {
        int requiredSlots = entryCount * 3 / 2;
        foreach (uint candidate in PossibleSizes)
        {
            if (candidate > requiredSlots)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("HashMap test helper does not support this many entries.");
    }

    private MockMemorySpace.HeapFragment AllocateFragment(ulong size, string? name)
    {
        byte[] initialData = new byte[checked((int)size)];
        if (!_allocator.TryAllocate(initialData, name, _allocator.MinAlign, out MockMemorySpace.HeapFragment? fragment))
        {
            throw new InvalidOperationException("Failed to allocate");
        }

        if (!_allocator.TracksAllocatedFragments)
        {
            _builder.AddHeapFragment(fragment);
        }

        return fragment;
    }

    private bool TryGetContainingFragment(ulong address, int size, out MockMemorySpace.HeapFragment? fragment, out int offset)
    {
        foreach (MockMemorySpace.HeapFragment allocation in _allocator.Allocations)
        {
            ulong allocationEnd = allocation.Address + (ulong)allocation.Data.Length;
            ulong requestedEnd = address + (ulong)size;
            if (address >= allocation.Address && requestedEnd <= allocationEnd)
            {
                fragment = allocation;
                offset = checked((int)(address - allocation.Address));
                return true;
            }
        }

        fragment = null;
        offset = 0;
        return false;
    }

    private bool TryAddEntryToBucket(MockHashMapBucket bucket, ulong key, ulong value)
    {
        for (int i = 0; i < bucket.SlotCount; i++)
        {
            if (bucket.GetKey(i) != 0)
            {
                continue;
            }

            bucket.SetKey(i, key);
            bucket.SetValue(i, value);
            return true;
        }

        bucket.SetValue(0, bucket.GetValue(0) | ~_valueMask);
        bucket.SetValue(1, bucket.GetValue(1) & _valueMask);

        return false;
    }

    private static void HashFunction(ulong key, uint size, out uint seed, out uint increment)
    {
        seed = (uint)(key >> 2);
        increment = (uint)(1 + (((uint)(key >> 5) + 1) % (size - 1)));
    }
}

public static class MockHashMapBuilderExtensions
{
    public static MockProcessBuilder AddHashMap(
        this MockProcessBuilder processBuilder,
        Action<MockHashMapBuilder> configure)
    {
        MockHashMapBuilder hashMapBuilder = new(
            processBuilder.MemoryBuilder,
            processBuilder.MemoryBuilder.DefaultAllocator);
        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptor =>
            {
                descriptor.AddType(hashMapBuilder.HashMapLayout);
                descriptor.AddType(hashMapBuilder.BucketLayout);
                descriptor
                    .AddGlobalValue("HashMapSlotsPerBucket", MockHashMapBucket.SlotsPerBucket)
                    .AddGlobalValue("HashMapValueMask", MockHashMapBuilder.GetValueMask(processBuilder.Architecture));
            });
        });

        configure(hashMapBuilder);

        return processBuilder;
    }
}
