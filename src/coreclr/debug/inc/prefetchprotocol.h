// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef DEBUGGER_PREFETCH_PROTOCOL_H
#define DEBUGGER_PREFETCH_PROTOCOL_H
#include <stdint.h>

namespace DebuggerPrefetchProtocol
{
    constexpr uint32_t Version = 2;
    constexpr uint32_t LineSize = 64;
    constexpr uint32_t RecordSize = sizeof(uint64_t) + LineSize;
    constexpr uint32_t MaximumLines = 131072;
    constexpr uint32_t MaximumPayloadSize = MaximumLines * RecordSize;
    constexpr uint32_t HandleObject = 1;
    constexpr uint32_t HandleCommon = 2;

    constexpr bool IsValidReply(uint32_t version, uint32_t lineSize, uint32_t lineCount,
        uint32_t payloadSize, uint32_t receivedSize, bool success)
    {
        return version == Version && lineSize == LineSize && lineCount <= MaximumLines &&
            payloadSize == lineCount * RecordSize && receivedSize == payloadSize &&
            (success || payloadSize == 0);
    }

    constexpr bool IsValidHandleRequest(uint32_t version, uint32_t flags)
    {
        return (version == 0 && flags == 0) ||
            (version == Version && (flags == HandleObject || flags == (HandleObject | HandleCommon)));
    }

    constexpr bool IsValidHandleReply(uint32_t version, uint32_t lineSize, uint32_t lineCount,
        uint32_t commonLineCount, uint32_t payloadSize, uint32_t receivedSize, bool success)
    {
        return (version == 0 && lineSize == 0 && lineCount == 0 && commonLineCount == 0 &&
                payloadSize == 0 && receivedSize == 0) ||
            (commonLineCount <= lineCount &&
                IsValidReply(version, lineSize, lineCount, payloadSize, receivedSize, success));
    }
}
#endif
