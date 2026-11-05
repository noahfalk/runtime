// Diagnostic-only ICorDebug call timing for the isolated WASM experiment.
#ifndef WASMDBG_API_TRACE_H
#define WASMDBG_API_TRACE_H

#ifdef _WIN32
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cwchar>

class WasmDbgApiTrace
{
    struct Header
    {
        char magic[8];
        DWORD version;
        DWORD headerSize;
        DWORD recordSize;
        DWORD capacity;
        volatile LONG used;
        volatile LONG dropped;
        LONGLONG frequency;
        LONGLONG anchorQpc;
        ULONGLONG anchorFileTime;
        DWORD pid;
        DWORD clockSampleTicks;
    };

    struct Record
    {
        LONGLONG start;
        volatile LONGLONG end;
        ULONGLONG parent;
        ULONGLONG object;
        DWORD thread;
        DWORD kind;
        ULONGLONG arg0;
        ULONGLONG arg1;
        LONGLONG result;
        ULONGLONG detail;
        DWORD flags;
        DWORD reserved;
        char function[432];
    };

    struct State
    {
        INIT_ONCE once = INIT_ONCE_STATIC_INIT;
        Header* header = nullptr;
        Record* records = nullptr;
    };

    static State& GetState()
    {
        static State state;
        return state;
    }

    static DWORD& CurrentCall()
    {
        static thread_local DWORD current = 0;
        return current;
    }

    static void Fail(const char* operation)
    {
        fprintf(stderr, "WASMDBG API trace failed: %s, error=%lu\n", operation, GetLastError());
        abort();
    }

    static BOOL CALLBACK Initialize(PINIT_ONCE, PVOID parameter, PVOID*)
    {
        State& state = *static_cast<State*>(parameter);
        WCHAR directory[1024];
        DWORD length = GetEnvironmentVariableW(L"DOTNET_WASMDBG_ICD_TRACE_DIR", directory, 1024);
        if (length == 0)
        {
            if (GetLastError() != ERROR_ENVVAR_NOT_FOUND)
                Fail("GetEnvironmentVariableW");
            return TRUE;
        }
        if (length >= 1024)
            Fail("trace directory too long");
        WCHAR filename[1200];
        if (_snwprintf_s(filename, 1200, _TRUNCATE, L"%s\\icordebug-%lu.bin", directory, GetCurrentProcessId()) < 0)
            Fail("trace filename");
        HANDLE file = CreateFileW(filename, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
            Fail("CreateFileW");
        const DWORD capacity = 262144;
        const DWORD bytes = sizeof(Header) + capacity * sizeof(Record);
        HANDLE mapping = CreateFileMappingW(file, nullptr, PAGE_READWRITE, 0, bytes, nullptr);
        if (mapping == nullptr)
            Fail("CreateFileMappingW");
        void* view = MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, bytes);
        if (view == nullptr)
            Fail("MapViewOfFile");
        CloseHandle(mapping);
        CloseHandle(file);
        Header* header = static_cast<Header*>(view);
        memcpy(header->magic, "ICDTRC1", 8);
        header->version = 2;
        header->headerSize = sizeof(Header);
        header->recordSize = sizeof(Record);
        header->capacity = capacity;
        LARGE_INTEGER frequency, before, after;
        FILETIME time;
        if (!QueryPerformanceFrequency(&frequency) || !QueryPerformanceCounter(&before))
            Fail("QueryPerformanceCounter anchor");
        GetSystemTimePreciseAsFileTime(&time);
        if (!QueryPerformanceCounter(&after))
            Fail("QueryPerformanceCounter anchor end");
        header->frequency = frequency.QuadPart;
        header->anchorQpc = before.QuadPart + (after.QuadPart - before.QuadPart) / 2;
        header->anchorFileTime = (static_cast<ULONGLONG>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
        header->pid = GetCurrentProcessId();
        header->clockSampleTicks = static_cast<DWORD>(after.QuadPart - before.QuadPart);
        state.records = reinterpret_cast<Record*>(header + 1);
        state.header = header;
        return TRUE;
    }

    Record* m_record = nullptr;
    DWORD m_parent = 0;

public:
    static bool LogicalReadTracingEnabled()
    {
        static const bool enabled =
            GetEnvironmentVariableW(L"DOTNET_WASMDBG_ICD_SKIP_READS", nullptr, 0) == 0;
        return enabled;
    }

    static bool Enabled()
    {
        State& state = GetState();
        if (!InitOnceExecuteOnce(&state.once, Initialize, &state, nullptr))
            Fail("InitOnceExecuteOnce");
        return state.header != nullptr;
    }

    WasmDbgApiTrace(const char* function, const void* object, DWORD kind = 0,
                   ULONGLONG arg0 = 0, ULONGLONG arg1 = 0)
    {
        static_assert(sizeof(Header) == 64);
        static_assert(sizeof(Record) == 512);
        State& state = GetState();
        if (!InitOnceExecuteOnce(&state.once, Initialize, &state, nullptr))
            Fail("InitOnceExecuteOnce");
        if (state.header == nullptr)
            return;
        LONG index = InterlockedIncrement(&state.header->used) - 1;
        size_t length = strlen(function);
        if (index < 0 || static_cast<DWORD>(index) >= state.header->capacity ||
            length >= sizeof(Record::function))
        {
            InterlockedIncrement(&state.header->dropped);
            return;
        }
        m_record = &state.records[index];
        m_parent = CurrentCall();
        m_record->parent = m_parent;
        m_record->object = reinterpret_cast<UINT_PTR>(object);
        m_record->thread = GetCurrentThreadId();
        m_record->kind = kind;
        m_record->arg0 = arg0;
        m_record->arg1 = arg1;
        memcpy(m_record->function, function, length + 1);
        CurrentCall() = static_cast<DWORD>(index + 1);
        LARGE_INTEGER start;
        if (!QueryPerformanceCounter(&start))
            Fail("QueryPerformanceCounter entry");
        m_record->start = start.QuadPart;
    }

    void SetResult(LONGLONG result, ULONGLONG detail = 0)
    {
        if (m_record != nullptr)
        {
            m_record->result = result;
            m_record->detail = detail;
            m_record->flags = 3;
        }
    }

    ~WasmDbgApiTrace()
    {
        if (m_record == nullptr)
            return;
        LARGE_INTEGER end;
        if (!QueryPerformanceCounter(&end))
            Fail("QueryPerformanceCounter exit");
        InterlockedExchange64(&m_record->end, end.QuadPart);
        CurrentCall() = m_parent;
    }
};

#define WASMDBG_API_TRACE() WasmDbgApiTrace __wasmDbgApiTrace(__FUNCTION__, this)
#define WASMDBG_API_TRACE_OBJECT(object, kind) WasmDbgApiTrace __wasmDbgApiTrace(__FUNCTION__, object, kind)
#else
#define WASMDBG_API_TRACE()
#define WASMDBG_API_TRACE_OBJECT(object, kind)
#endif

#endif
