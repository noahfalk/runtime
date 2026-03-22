// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

/// <summary>
/// Helper for creating a mock memory space for testing.
/// </summary>
/// <remarks>
/// Use MockMemorySpace.CreateContext to create a mostly empty context for reading from the target.
/// Use MockMemorySpace.ContextBuilder to create a context with additional MockMemorySpace.HeapFragment data.
/// </remarks>
public static unsafe partial class MockMemorySpace
{
    public class BumpAllocator
    {
        private readonly ulong _blockStart;
        private readonly ulong _blockEnd; // exclusive
        private readonly MockTarget.Architecture _architecture;
        private ulong _current;

        public ulong MinAlign { get; init; } = 16; // by default align to 16 bytes
        public BumpAllocator(ulong blockStart, ulong blockEnd, MockTarget.Architecture architecture, bool tracksAllocatedFragments = false)
        {
            _blockStart = blockStart;
            _blockEnd = blockEnd;
            _architecture = architecture;
            _current = blockStart;
            TracksAllocatedFragments = tracksAllocatedFragments;
        }

        public ulong RangeStart => _blockStart;
        public ulong RangeEnd => _blockEnd;
        public ulong Current => _current;
        public bool TracksAllocatedFragments { get; }

        public List<HeapFragment> Allocations { get; } = [];

        private static ulong AlignUp(ulong value, ulong alignment)
        {
            return (value + (alignment - 1)) & ~(alignment - 1);
        }

        public void AdvanceAligned(ulong alignment)
        {
            _current = AlignUp(_current, alignment);
        }

        public void AdvanceTo(ulong address)
        {
            if (address < _current)
            {
                throw new InvalidOperationException($"Cannot move allocator backwards from 0x{_current:x} to 0x{address:x}.");
            }

            if (address > _blockEnd)
            {
                throw new InvalidOperationException($"Cannot advance allocator beyond range end 0x{_blockEnd:x}.");
            }

            _current = address;
        }

        public bool TryAllocate(byte[] initialData, string name, ulong alignment, [NotNullWhen(true)] out HeapFragment? fragment)
        {
            ulong current = AlignUp(_current, alignment);
            ulong size = (ulong)initialData.Length;
            Debug.Assert(current >= _current);
            Debug.Assert((current % (ulong)alignment) == 0);
            if (current + size <= _blockEnd)
            {
                fragment = new HeapFragment
                {
                    Address = current,
                    Data = initialData,
                    Name = name,
                };
                current += size;
                _current = current;
                Allocations.Add(fragment);
                return true;
            }
            fragment = null;
            return false;
        }
        private HeapFragment AllocateFragmentCore(byte[] initialData, string name, ulong alignment)
        {
            if (!TryAllocate(initialData, name, alignment, out HeapFragment? fragment))
            {
                throw new InvalidOperationException("Failed to allocate");
            }
            return fragment;
        }

        public HeapFragment AllocateFragment(byte[] initialData, string? name = null, ulong? alignment = null)
            => AllocateFragmentCore(initialData, name ?? "fragment", alignment ?? MinAlign);

        public HeapFragment AllocateFragment(ulong size, string? name = null, ulong? alignment = null)
            => AllocateFragment(new byte[checked((int)size)], name, alignment);

        public HeapFragment AllocateFragment(ReadOnlySpan<byte> content, string? name = null, ulong? alignment = null)
            => AllocateFragment(content.ToArray(), name, alignment);

        public ulong AllocateInt32(int value, string? name = null)
        {
            HeapFragment fragment = AllocateFragment(sizeof(int), name ?? $"int32({value})");
            SpanWriter writer = new(_architecture, fragment.Data);
            writer.Write(value);
            return fragment.Address;
        }

        public ulong AllocateUInt32(uint value, string? name = null)
        {
            HeapFragment fragment = AllocateFragment(sizeof(uint), name ?? $"uint32({value})");
            SpanWriter writer = new(_architecture, fragment.Data);
            writer.Write(value);
            return fragment.Address;
        }

        public ulong AllocateInt64(long value, string? name = null)
        {
            HeapFragment fragment = AllocateFragment(sizeof(long), name ?? $"int64({value})");
            SpanWriter writer = new(_architecture, fragment.Data);
            writer.Write(unchecked((ulong)value));
            return fragment.Address;
        }

        public ulong AllocateUInt64(ulong value, string? name = null)
        {
            HeapFragment fragment = AllocateFragment(sizeof(ulong), name ?? $"uint64({value})");
            SpanWriter writer = new(_architecture, fragment.Data);
            writer.Write(value);
            return fragment.Address;
        }

        public ulong AllocatePointer(ulong pointerValue, string? name = null)
        {
            HeapFragment fragment = AllocateFragment((ulong)_architecture.PointerSize, name ?? $"ptr(0x{pointerValue:x})");
            SpanWriter writer = new(_architecture, fragment.Data);
            writer.WritePointer(pointerValue);
            return fragment.Address;
        }

        public bool Overlaps(BumpAllocator other)
        {
            if ((other._blockStart <= _blockStart && other._blockEnd > _blockStart) ||
                (other._blockStart >= _blockStart && other._blockStart < _blockEnd))
            {
                return true;
            }
            return false;
        }
    }
}
