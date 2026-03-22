// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks
{
    public ref struct SpanWriter
    {
        public SpanWriter(MockTarget.Architecture architecture, Span<byte> buffer)
        {
            Architecture = architecture;
            Buffer = buffer;
            _remainingBuffer = buffer;
        }

        public MockTarget.Architecture Architecture { get; }
        public Span<byte> Buffer { get; }

        private Span<byte> _remainingBuffer;

        internal void Write(uint value)
        {
            if (Architecture.IsLittleEndian)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(_remainingBuffer, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(_remainingBuffer, value);
            }
            _remainingBuffer = _remainingBuffer.Slice(sizeof(uint));
        }

        internal void Write(int value)
        {
            if (Architecture.IsLittleEndian)
            {
                BinaryPrimitives.WriteInt32LittleEndian(_remainingBuffer, value);
            }
            else
            {
                BinaryPrimitives.WriteInt32BigEndian(_remainingBuffer, value);
            }
            _remainingBuffer = _remainingBuffer.Slice(sizeof(int));
        }

        internal void Write(ulong value)
        {
            if (Architecture.IsLittleEndian)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(_remainingBuffer, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(_remainingBuffer, value);
            }
            _remainingBuffer = _remainingBuffer.Slice(sizeof(ulong));
        }

        internal void WritePointer(ulong value)
        {
            if (Architecture.Is64Bit)
            {
                Write(value);
            }
            else
            {
                Write((uint)value);
            }
        }
    }
}
