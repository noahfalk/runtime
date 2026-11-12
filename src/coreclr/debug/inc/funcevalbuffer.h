// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef FUNCEVALBUFFER_H
#define FUNCEVALBUFFER_H

#include <stdint.h>

namespace DebuggerFuncEvalBuffer
{
    constexpr uint32_t Version = 1;
    constexpr uint32_t InitialCapacity = 4096;
    constexpr uint32_t MaximumCapacity = 8 * 1024 * 1024;
    constexpr uint32_t Alignment = 8;
    constexpr uint32_t AbortRequested = 1;

    struct State
    {
        uint64_t address;
        uint32_t capacity;
        uint32_t processed;
        uint32_t requestedCapacity;
        uint32_t generation;
    };

    inline bool IsValidRecord(uint32_t bytes, uint32_t headerBytes, uint32_t dataBytes,
                              uint32_t available)
    {
        return bytes >= headerBytes && bytes % Alignment == 0 &&
            bytes <= available && available - bytes >= sizeof(uint32_t) &&
            dataBytes <= bytes - headerBytes && bytes - headerBytes - dataBytes < Alignment;
    }

    inline uint32_t RecordSize(uint32_t headerBytes, uint32_t dataBytes)
    {
        if (headerBytes > MaximumCapacity - Alignment || dataBytes > MaximumCapacity - headerBytes - Alignment)
            return 0;
        return (headerBytes + dataBytes + Alignment - 1) & ~(Alignment - 1);
    }
}

#endif
