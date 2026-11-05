#ifndef WASMDBG_EXACT_PROFILE_H
#define WASMDBG_EXACT_PROFILE_H

#ifdef _WIN32
#include <cstdio>
#include <cstdlib>

class WasmDbgExactReadProfile
{
    struct Totals
    {
        LONGLONG start, lookup = 0, inner = 0;
        ULONGLONG calls = 0, bytes = 0, hits = 0, hitBytes = 0;
    } m_totals;
    Totals* m_previous = nullptr;
    WCHAR m_directory[1024] = {};
    static Totals*& Current()
    {
        static thread_local Totals* current = nullptr;
        return current;
    }
    static LONGLONG Now()
    {
        LARGE_INTEGER time;
        if (!QueryPerformanceCounter(&time)) abort();
        return time.QuadPart;
    }
public:
    WasmDbgExactReadProfile()
    {
        DWORD length = GetEnvironmentVariableW(L"DOTNET_WASMDBG_EXACT_PROFILE_DIR", m_directory, 1024);
        if (length == 0) return;
        if (length >= 1024) abort();
        m_previous = Current();
        m_totals.start = Now();
        Current() = &m_totals;
    }
    ~WasmDbgExactReadProfile()
    {
        if (m_directory[0] == 0) return;
        LONGLONG end = Now();
        Current() = m_previous;
        WCHAR filename[1200];
        if (_snwprintf_s(filename, 1200, _TRUNCATE, L"%s\\dbi-exact-%lu.jsonl",
            m_directory, GetCurrentProcessId()) < 0) abort();
        HANDLE file = CreateFileW(filename, FILE_APPEND_DATA, FILE_SHARE_READ, nullptr,
            OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) abort();
        LARGE_INTEGER frequency;
        if (!QueryPerformanceFrequency(&frequency)) abort();
        char line[1024];
        int length = sprintf_s(line,
            "{\"pid\":%lu,\"thread\":%lu,\"frequency\":%lld,\"start\":%lld,\"end\":%lld,"
            "\"reads\":%llu,\"bytes\":%llu,\"lineHits\":%llu,\"lineHitBytes\":%llu,"
            "\"lookupTicks\":%lld,\"innerTicks\":%lld}\n",
            GetCurrentProcessId(), GetCurrentThreadId(), frequency.QuadPart, m_totals.start, end,
            m_totals.calls, m_totals.bytes, m_totals.hits, m_totals.hitBytes, m_totals.lookup, m_totals.inner);
        DWORD written = 0;
        if (length <= 0 || !WriteFile(file, line, length, &written, nullptr) || written != static_cast<DWORD>(length))
        {
            fprintf(stderr, "WASMDBG exact-read profile write failed: %lu\n", GetLastError());
            abort();
        }
        CloseHandle(file);
    }
    class Read
    {
        Totals* m_current;
        LONGLONG m_start = 0, m_inner = 0;
        ULONG32 m_bytes;
    public:
        Read(ULONG32 bytes) : m_current(Current()), m_bytes(bytes)
        {
            if (m_current != nullptr)
            {
                m_current->calls++;
                m_current->bytes += bytes;
                m_start = Now();
            }
        }
        void Hit()
        {
            if (m_current != nullptr)
            {
                m_current->hits++;
                m_current->hitBytes += m_bytes;
            }
        }
        void Fallback()
        {
            if (m_current != nullptr) m_inner = Now();
        }
        ~Read()
        {
            if (m_current == nullptr) return;
            LONGLONG end = Now();
            m_current->lookup += (m_inner == 0 ? end : m_inner) - m_start;
            if (m_inner != 0) m_current->inner += end - m_inner;
        }
    };
};
#else
class WasmDbgExactReadProfile
{
public:
    class Read
    {
    public:
        Read(ULONG32) {}
        void Hit() {}
        void Fallback() {}
    };
};
#endif
#endif
