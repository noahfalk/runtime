// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef PREFETCH_DATA_TARGET_H
#define PREFETCH_DATA_TARGET_H
#include "prefetchlinecache.h"
#include "wasmdbgexactprofile.h"

class PrefetchDataTarget final : public ICorDebugMutableDataTarget
{
    ICorDebugDataTarget* m_inner;
    ICorDebugMutableDataTarget* m_mutable = nullptr;
    LONG m_refs = 0;
    RSLock* m_lock;
    UINT* m_continueCounter;
    UINT* m_flushCounter;
    UINT m_epoch = 0;
    UINT m_stackEpoch = 0;
    PrefetchLineCache m_cache;
    PrefetchLineCache m_stackCache;
    PrefetchLineCache m_commonCache;
    bool m_commonPopulated = false;
    std::vector<VMPTR_Assembly> m_assemblies;
    std::vector<VMPTR_Thread> m_threads;
    std::vector<UINT_PTR> m_fieldTypes;

public:
    PrefetchDataTarget(ICorDebugDataTarget* inner, RSLock* lock, UINT* counter, UINT* flushCounter)
        : m_inner(inner), m_lock(lock), m_continueCounter(counter), m_flushCounter(flushCounter)
    {
        HRESULT hr = inner->QueryInterface(IID_ICorDebugMutableDataTarget, reinterpret_cast<void**>(&m_mutable));
        if (FAILED(hr) && hr != E_NOINTERFACE)
            ThrowHR(hr);
        inner->AddRef();
    }
    ~PrefetchDataTarget()
    {
        if (m_mutable != nullptr) m_mutable->Release();
        m_inner->Release();
    }
    void Invalidate()
    {
        m_cache.Clear();
        m_stackCache.Clear();
        m_commonCache.Clear();
        m_commonPopulated = false;
        m_assemblies.clear();
        m_threads.clear();
        m_fieldTypes.clear();
        m_epoch = *m_continueCounter;
        m_stackEpoch = *m_flushCounter;
    }
    void EnsureEpoch()
    {
        if (m_epoch != *m_continueCounter)
            Invalidate();
        if (m_stackEpoch != *m_flushCounter)
            InvalidateStack();
    }
    void InvalidateStack()
    {
        RSLockHolder holder(m_lock);
        m_stackCache.Clear();
        m_threads.clear();
        m_fieldTypes.clear();
        m_stackEpoch = *m_flushCounter;
    }
    bool ContainsFieldType(UINT_PTR type)
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        for (UINT_PTR existing : m_fieldTypes)
            if (existing == type)
                return true;
        return false;
    }
    bool PopulateFieldType(UINT_PTR type, const PrefetchLineCache::Line* lines, UINT32 count, bool complete)
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        if (!m_stackCache.Add(lines, count))
            return false;
        if (complete)
            m_fieldTypes.push_back(type);
        return true;
    }
    bool ContainsAssembly(VMPTR_Assembly assembly)
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        for (VMPTR_Assembly existing : m_assemblies)
            if (existing == assembly)
                return true;
        return false;
    }
    void InvalidateCommon()
    {
        RSLockHolder holder(m_lock);
        m_commonCache.Clear();
        m_commonPopulated = false;
    }
    bool ContainsCommon()
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        return m_commonPopulated;
    }
    bool PopulateCommon(const PrefetchLineCache::Line* lines, UINT32 count)
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        if (!m_commonCache.Add(lines, count))
            return false;
        m_commonPopulated = true;
        return true;
    }
    bool Populate(VMPTR_Assembly assembly, const PrefetchLineCache::Line* lines, UINT32 count)
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        if (!m_cache.Add(lines, count))
            return false;
        m_assemblies.push_back(assembly);
        return true;
    }
    ICorDebugDataTarget* Inner() { return m_inner; }
    bool ContainsThread(VMPTR_Thread thread)
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        for (VMPTR_Thread existing : m_threads)
            if (existing == thread)
                return true;
        return false;
    }
    bool PopulateThread(VMPTR_Thread thread, const PrefetchLineCache::Line* lines, UINT32 count)
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        if (!m_stackCache.Add(lines, count))
            return false;
        m_threads.push_back(thread);
        return true;
    }
    bool PopulateHandle(const PrefetchLineCache::Line* lines, UINT32 count, UINT32 commonCount, bool includesCommon)
    {
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        if (commonCount > count || (!includesCommon && commonCount != 0))
            return false;
        // Object headers and handle slots expire on every DAC flush, like stack snapshots.
        if (!m_stackCache.Add(commonCount == 0 ? lines : lines + commonCount, count - commonCount))
            return false;
        if (includesCommon)
        {
            if (!m_commonCache.Add(lines, commonCount))
                return false;
            m_commonPopulated = true;
        }
        return true;
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
    {
        if (value == nullptr) return E_POINTER;
        if (iid == IID_IUnknown || iid == IID_ICorDebugDataTarget ||
            (iid == IID_ICorDebugMutableDataTarget && m_mutable != nullptr))
        {
            *value = static_cast<ICorDebugMutableDataTarget*>(this);
            AddRef();
            return S_OK;
        }
        return m_inner->QueryInterface(iid, value);
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&m_refs); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        ULONG refs = InterlockedDecrement(&m_refs);
        if (refs == 0) delete this;
        return refs;
    }
    HRESULT STDMETHODCALLTYPE GetPlatform(CorDebugPlatform* platform) override { return m_inner->GetPlatform(platform); }
    HRESULT STDMETHODCALLTYPE ReadVirtual(CORDB_ADDRESS address, BYTE* buffer, ULONG32 count, ULONG32* read) override
    {
        if (read == nullptr || (buffer == nullptr && count != 0)) return E_POINTER;
        WasmDbgExactReadProfile::Read profile(count);
        RSLockHolder holder(m_lock);
        EnsureEpoch();
        if (m_stackCache.Read(address, buffer, count) || m_commonCache.Read(address, buffer, count) ||
            m_cache.Read(address, buffer, count))
        {
            profile.Hit();
            *read = count;
            return S_OK;
        }
        profile.Fallback();
        return m_inner->ReadVirtual(address, buffer, count, read);
    }
    HRESULT STDMETHODCALLTYPE GetThreadContext(DWORD thread, ULONG32 flags, ULONG32 size, BYTE* context) override
    { return m_inner->GetThreadContext(thread, flags, size, context); }
    HRESULT STDMETHODCALLTYPE WriteVirtual(CORDB_ADDRESS address, const BYTE* buffer, ULONG32 size) override
    {
        RSLockHolder holder(m_lock);
        Invalidate();
        return m_mutable == nullptr ? E_NOINTERFACE : m_mutable->WriteVirtual(address, buffer, size);
    }
    HRESULT STDMETHODCALLTYPE SetThreadContext(DWORD thread, ULONG32 size, const BYTE* context) override
    {
        RSLockHolder holder(m_lock);
        Invalidate();
        return m_mutable == nullptr ? E_NOINTERFACE : m_mutable->SetThreadContext(thread, size, context);
    }
    HRESULT STDMETHODCALLTYPE ContinueStatusChanged(DWORD thread, CORDB_CONTINUE_STATUS status) override
    { return m_mutable == nullptr ? E_NOINTERFACE : m_mutable->ContinueStatusChanged(thread, status); }
};
#endif
