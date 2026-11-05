// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef PREFETCH_LINE_CACHE_H
#define PREFETCH_LINE_CACHE_H

#include <cstdint>
#include <cstring>
#include <vector>
#include <limits>
#include "prefetchprotocol.h"

class PrefetchLineCache
{
public:
    static constexpr uint32_t LineSize = DebuggerPrefetchProtocol::LineSize;
    static constexpr uint32_t MaximumLines = DebuggerPrefetchProtocol::MaximumLines;
    struct Line
    {
        uint64_t address;
        uint8_t bytes[LineSize];
    };
    static_assert(sizeof(Line) == DebuggerPrefetchProtocol::RecordSize);

private:
    std::vector<Line> m_lines;
    std::vector<uint32_t> m_slots;

    size_t Hash(uint64_t address) const
    {
        return static_cast<size_t>(((address / LineSize) * UINT64_C(11400714819323198485)) >> 32) & (m_slots.size() - 1);
    }

    const Line* Find(uint64_t address) const
    {
        size_t slot = Hash(address);
        while (uint32_t index = m_slots[slot])
        {
            const Line& line = m_lines[index - 1];
            if (line.address == address)
                return &line;
            slot = (slot + 1) & (m_slots.size() - 1);
        }
        return nullptr;
    }

public:
    void Clear()
    {
        m_slots.clear();
        m_lines.clear();
    }

    bool Add(const Line* lines, uint32_t count)
    {
        if (count > MaximumLines - m_lines.size())
            return false;
        for (uint32_t i = 0; i < count; i++)
            if ((lines[i].address & (LineSize - 1)) != 0 ||
                lines[i].address > std::numeric_limits<uint64_t>::max() - LineSize)
                return false;
        if (count == 0)
            return true;
        m_lines.insert(m_lines.end(), lines, lines + count);
        size_t capacity = 16;
        while (capacity < m_lines.size() * 2)
            capacity *= 2;
        m_slots.assign(capacity, 0);
        for (size_t i = 0; i < m_lines.size(); i++)
        {
            size_t slot = Hash(m_lines[i].address);
            while (m_slots[slot] && m_lines[m_slots[slot] - 1].address != m_lines[i].address)
                slot = (slot + 1) & (capacity - 1);
            m_slots[slot] = static_cast<uint32_t>(i + 1);
        }
        return true;
    }

    bool Read(uint64_t address, uint8_t* buffer, uint32_t count) const
    {
        if (m_slots.empty() || count == 0 || address > std::numeric_limits<uint64_t>::max() - count)
            return false;
        uint64_t first = address & ~uint64_t(LineSize - 1);
        const Line* line = Find(first);
        if (line == nullptr)
            return false;
        uint32_t offset = static_cast<uint32_t>(address - first);
        if (count <= LineSize - offset)
        {
            memcpy(buffer, line->bytes + offset, count);
            return true;
        }
        for (uint64_t next = first + LineSize; next < address + count; next += LineSize)
            if (Find(next) == nullptr)
                return false;
        while (count != 0)
        {
            uint32_t available = LineSize - offset;
            uint32_t copied = count < available ? count : available;
            memcpy(buffer, line->bytes + offset, copied);
            count -= copied;
            buffer += copied;
            first += LineSize;
            offset = 0;
            if (count != 0)
                line = Find(first);
        }
        return true;
    }
};
#endif
