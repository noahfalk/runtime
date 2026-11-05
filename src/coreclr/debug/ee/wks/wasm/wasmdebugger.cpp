// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "stdafx.h"
#include "../../controller.h"
#include "../../debugger.h"
#include "dbgtransportsession.h"
#include "interpexec.h"
#include <emscripten.h>
#include <emscripten/heap.h>
#include "typehash.h"
#include "class.h"
#include "codeman.h"
#include "debuginfostore.h"
#include "nibblemapmacros.h"
#include "peimagelayout.h"
#include "cdacplatformmetadata.hpp"
#include "exinfo.h"

#include "prefetchprotocol.h"
#include "shash.h"
#include "typekey.h"
#include "binder.h"
#include "loaderallocator.hpp"

class WasmMemoryCollector
{
    SetSHash<UINT32> m_seen;
    SArray<UINT32> m_lines;
    SArray<Module*> m_stackModules;
    SArray<MethodDesc*> m_stackMethods;
    SetSHash<TADDR> m_fieldTypes;
    SetSHash<UINT64> m_fieldBuckets;
    bool m_fieldsComplete = true;
    UINT32 m_fieldNodes = 0;
    size_t m_heapSize;

public:
    static void ValidateThread(Thread* thread)
    {
        // The single-threaded debugger runs synchronously on the stopped runtime
        // thread. The store cannot change here; taking its lock under the debugger
        // lock would invert the normal lock order at function-evaluation stops.
        _ASSERTE(g_pDebugger->IsStopped());
        ThreadStore* store = ThreadStore::s_pThreadStore;
        for (Thread* candidate = store->m_ThreadList.GetHead(); candidate != nullptr;
            candidate = store->m_ThreadList.GetNext(candidate))
        {
            if (candidate == thread && !candidate->IsDead())
                return;
        }
        ThrowHR(CORDBG_E_BAD_THREAD_STATE);
    }
    WasmMemoryCollector() : m_heapSize(emscripten_get_heap_size())
    {
    }
    void Add(const void* pointer, size_t size)
    {
        if (size == 0) return;
        size_t address = reinterpret_cast<size_t>(pointer);
        if (address == 0 || size > m_heapSize || address > m_heapSize - size)
            ThrowHR(E_INVALIDARG);
        for (size_t line = address & ~size_t(DebuggerPrefetchProtocol::LineSize - 1);
            line < address + size; line += DebuggerPrefetchProtocol::LineSize)
        {
            // Reserve zero for the hash table's empty sentinel, including for the first heap line.
            UINT32 key = static_cast<UINT32>(line / DebuggerPrefetchProtocol::LineSize) + 1;
            if (m_seen.Contains(key)) continue;
            if (m_lines.GetCount() == DebuggerPrefetchProtocol::MaximumLines) ThrowHR(E_OUTOFMEMORY);
            m_seen.Add(key);
            m_lines.Append(static_cast<UINT32>(line));
        }
    }
    void AddType(TypeHandle handle, bool instantiate)
    {
        if (handle.IsTypeDesc())
        {
            Add(handle.AsTypeDesc(), sizeof(TypeDesc));
            return;
        }
        MethodTable* mt = handle.AsMethodTable();
        Add(mt, sizeof(MethodTable));
        MethodTable* canonical = mt->GetCanonicalMethodTable();
        Add(canonical, sizeof(MethodTable));
        EEClass* klass = mt->GetClass();
        Add(klass, sizeof(EEClass));
        Add(klass->GetMethodTable(), sizeof(MethodTable));
        Add(klass->GetMethodTable()->GetClass(), sizeof(EEClass));
        Add(mt->GetAuxiliaryData(), sizeof(MethodTableAuxiliaryData));
        if (!instantiate || !mt->HasInstantiation()) return;
        Add(mt->GetGenericsDictInfo(), sizeof(GenericsDictInfo));
        Add(mt->GetPerInstInfo(), mt->GetNumDicts() * sizeof(void*));
        Instantiation inst = mt->GetInstantiation();
        Add(inst.GetRawArgs(), inst.GetNumArgs() * sizeof(TypeHandle));
        for (DWORD i = 0; i < inst.GetNumArgs(); i++)
            AddType(inst[i], false);
    }
    UINT32 Count() const { return static_cast<UINT32>(m_lines.GetCount()); }
    void AddObjectHandle(OBJECTHANDLE handle)
    {
        Add(handle, sizeof(*handle));
        Object* object = OBJECTREFToObject(ObjectFromHandle(handle));
        if (object == nullptr)
            return;
        MethodTable* mt = object->GetMethodTable();
        Add(object, mt->IsArray() ? ArrayBase::GetDataPtrOffset(mt) :
            mt->IsString() ? sizeof(StringObject) : sizeof(Object));
        Add(&g_pStringClass, sizeof(g_pStringClass));
        Add(&g_pObjectClass, sizeof(g_pObjectClass));
        Add(&g_pFreeObjectMethodTable, sizeof(g_pFreeObjectMethodTable));

        AddTypeGraph(TypeHandle(mt));
    }
    void AddTypeGraph(TypeHandle root)
    {
        SArray<TypeHandle> types;
        SetSHash<TADDR> seenTypes;
        types.Append(root);
        for (COUNT_T i = 0; i < types.GetCount(); i++)
        {
            TypeHandle type = types[i];
            if (type.IsNull() || seenTypes.Contains(type.AsTAddr()))
                continue;
            seenTypes.Add(type.AsTAddr());
            AddType(type, true);
            if (!type.IsTypeDesc())
            {
                MethodTable* table = type.AsMethodTable();
                AddStackModule(table->GetModule());
                MethodTable* parent = table->GetParentMethodTable();
                if (parent != nullptr)
                    types.Append(TypeHandle(parent));
                Instantiation inst = table->GetInstantiation();
                if (types.GetCount() > 4096 || inst.GetNumArgs() > 4096 - types.GetCount())
                    ThrowHR(E_INVALIDARG);
                for (DWORD arg = 0; arg < inst.GetNumArgs(); arg++)
                    types.Append(inst[arg]);
            }
            if (type.HasTypeParam())
            {
                if (type.IsTypeDesc())
                    Add(type.AsTypeDesc(), sizeof(ParamTypeDesc));
                types.Append(type.GetTypeParam());
            }
            if (type.IsTypeDesc() && type.AsTypeDesc()->IsFnPtr())
            {
                FnPtrTypeDesc* function = static_cast<FnPtrTypeDesc*>(type.AsTypeDesc());
                if (function->GetNumArgs() >= 4096 - types.GetCount())
                    ThrowHR(E_INVALIDARG);
                Add(function, sizeof(*function));
                TypeHandle* signature = function->GetRetAndArgTypesPointer();
                Add(signature, (function->GetNumArgs() + 1) * sizeof(TypeHandle));
                for (DWORD arg = 0; arg <= function->GetNumArgs(); arg++)
                    types.Append(signature[arg]);
            }
            if (types.GetCount() > 4096)
                ThrowHR(E_INVALIDARG);
        }
    }

    void AddBasicResultType(const DebuggerIPCE_BasicTypeData& type)
    {
        if (!type.vmTypeHandle.IsNull())
            AddTypeGraph(TypeHandle::FromTAddr(reinterpret_cast<TADDR>(type.vmTypeHandle.GetRawPtr())));
        if (!type.vmAssembly.IsNull())
            AddStackModule(type.vmAssembly.GetRawPtr()->GetModule());
    }
    void AddResultType(const DebuggerIPCE_ExpandedTypeData& type)
    {
        switch (type.elementType)
        {
        case ELEMENT_TYPE_CLASS:
        case ELEMENT_TYPE_VALUETYPE:
            if (!type.ClassTypeData.typeHandle.IsNull())
                AddTypeGraph(TypeHandle::FromTAddr(reinterpret_cast<TADDR>(type.ClassTypeData.typeHandle.GetRawPtr())));
            if (!type.ClassTypeData.vmAssembly.IsNull())
                AddStackModule(type.ClassTypeData.vmAssembly.GetRawPtr()->GetModule());
            break;
        case ELEMENT_TYPE_ARRAY:
        case ELEMENT_TYPE_SZARRAY:
            AddBasicResultType(type.ArrayTypeData.arrayTypeArg);
            break;
        case ELEMENT_TYPE_PTR:
        case ELEMENT_TYPE_BYREF:
            AddBasicResultType(type.UnaryTypeData.unaryTypeArg);
            break;
        case ELEMENT_TYPE_FNPTR:
            if (!type.NaryTypeData.typeHandle.IsNull())
                AddTypeGraph(TypeHandle::FromTAddr(reinterpret_cast<TADDR>(type.NaryTypeData.typeHandle.GetRawPtr())));
            break;
        default:
            break;
        }
    }

    void AddObjectDereferenceMemory(OBJECTHANDLE handle)
    {
        Object* object = OBJECTREFToObject(ObjectFromHandle(handle));
        if (object == nullptr)
            return;
        MethodTable* mt = object->GetMethodTable();
        // Arrays initialize only their bounds; boxes defer copying their contents.
        if (mt->IsArray() || mt->IsValueType())
            return;
        if (!mt->IsString())
        {
            Add(&g_pExceptionClass, sizeof(g_pExceptionClass));
            Add(object->GetHeader(), sizeof(ObjHeader));
            SyncBlock* block = object->PassiveGetSyncBlock();
            if (block != nullptr)
            {
                Add(&g_pSyncTable, sizeof(g_pSyncTable));
                Add(&g_pSyncTable[block->GetSyncBlockIndex()], sizeof(SyncTableEntry));
                Add(block, sizeof(*block));
                InteropSyncBlockInfo* interop = block->GetInteropInfoNoCreate();
                if (interop != nullptr)
                    Add(interop, sizeof(*interop));
            }
        }

        // Object/string values copy the instance. Keep large results within the
        // existing reply budget; bytes outside the snapshot remain demand reads.
        size_t budget = (DebuggerPrefetchProtocol::MaximumLines - Count()) *
            size_t(DebuggerPrefetchProtocol::LineSize);
        size_t offset = reinterpret_cast<size_t>(object) & (DebuggerPrefetchProtocol::LineSize - 1);
        if (budget > offset)
            Add(object, min(object->GetSize(), budget - offset));
    }

    void AddLoaderMemory(Module* module)
    {
        AddStackModule(module);
        LoaderAllocator* allocator = module->GetLoaderAllocator();
        if (allocator != nullptr)
        {
            Add(reinterpret_cast<BYTE*>(allocator) + cdac_data<LoaderAllocator>::IsCollectible, sizeof(bool));
            Add(reinterpret_cast<BYTE*>(allocator) + cdac_data<LoaderAllocator>::CreationNumber, sizeof(UINT64));
        }
    }
    void AddFieldLayout(MethodTable* mt)
    {
        if (mt->IsDynamicStatics())
            Add(mt->GetDynamicStaticsInfo(), sizeof(DynamicStaticsInfo));
        ApproxFieldDescIterator fields(mt, ApproxFieldDescIterator::ALL_FIELDS);
        for (FieldDesc* field = fields.Next(); field != nullptr; field = fields.Next())
            Add(field, sizeof(*field));
    }
    void AddTypeToken(Module* module, mdToken token)
    {
        AddLoaderMemory(module);
        if (TypeFromToken(token) != mdtTypeDef && TypeFromToken(token) != mdtTypeRef)
        {
            m_fieldsComplete = false;
            return;
        }
        size_t offset = TypeFromToken(token) == mdtTypeDef ?
            cdac_data<Module>::TypeDefToMethodTableMap : cdac_data<Module>::TypeRefToMethodTableMap;
        DWORD rid = RidFromToken(token);
        UINT32 visited = 0;
        for (auto map = reinterpret_cast<LookupMapBase*>(reinterpret_cast<BYTE*>(module) + offset);
            map != nullptr; map = map->pNext)
        {
            if (++visited > 4096) ThrowHR(E_INVALIDARG);
            Add(map, sizeof(*map));
            if (rid < map->dwCount)
            {
                Add(map->GetIndexPtr(rid), sizeof(TADDR));
                break;
            }
            rid -= map->dwCount;
        }
        TypeHandle definition = TypeFromToken(token) == mdtTypeDef ?
            module->LookupTypeDef(token) : module->LookupTypeRef(token);
        if (!definition.IsNull() && definition.GetLoadLevel() == CLASS_LOADED)
        {
            // DBI also asks about the uninstantiated definition, not just its closed field type.
            AddTypeGraph(definition);
            if (!definition.IsTypeDesc())
                AddFieldLayout(definition.AsMethodTable());
        }
    }
    void AddFieldLookup(const TypeKey& key)
    {
        if (!key.IsConstructed()) return;
        Module* loader = ClassLoader::ComputeLoaderModule(&key);
        AddLoaderMemory(loader);
        DWORD hash = HashTypeKey(&key);
        UINT64 bucketKey = (static_cast<UINT64>(reinterpret_cast<size_t>(loader)) << 32) | hash;
        if (m_fieldBuckets.Contains(bucketKey)) return;
        m_fieldBuckets.Add(bucketKey);
        EETypeHashTable* table = loader->GetAvailableParamTypes();
        if (table != nullptr)
        {
            Add(table, sizeof(*table));
            table->VisitDebuggerBucketMemory(hash, *this);
        }
    }
    void AddFieldType(TypeHandle type, UINT32 depth = 0)
    {
        if (type.IsNull() || m_fieldTypes.Contains(type.AsTAddr())) return;
        if (depth > 64 || m_fieldTypes.GetCount() >= 4096) ThrowHR(E_INVALIDARG);
        m_fieldTypes.Add(type.AsTAddr());
        AddTypeGraph(type);
        AddLoaderMemory(type.GetLoaderModule());
        AddFieldLookup(type.GetTypeKey());
        if (!type.IsTypeDesc())
        {
            MethodTable* mt = type.AsMethodTable();
            AddTypeToken(mt->GetModule(), mt->GetCl());
            Instantiation inst = mt->GetInstantiation();
            for (DWORD i = 0; i < inst.GetNumArgs(); i++)
                AddFieldType(inst[i], depth + 1);
        }
        if (type.HasTypeParam())
            AddFieldType(type.GetTypeParam(), depth + 1);
    }
    TypeHandle AddFieldSignature(SigPointer& signature, Module* module,
                                const SigTypeContext& context, UINT32 depth = 0)
    {
        if (depth > 64 || ++m_fieldNodes > 16384) ThrowHR(E_INVALIDARG);
        SigPointer next = signature;
        IfFailThrow(next.SkipExactlyOne());
        TypeHandle type = signature.GetTypeHandleThrowing(module, &context, ClassLoader::DontLoadTypes);
        CorElementType element;
        IfFailThrow(signature.GetElemType(&element));
        if (element == ELEMENT_TYPE_CLASS || element == ELEMENT_TYPE_VALUETYPE)
        {
            mdToken token;
            IfFailThrow(signature.GetToken(&token));
            AddTypeToken(module, token);
        }
        else if (element == ELEMENT_TYPE_GENERICINST)
        {
            SigPointer definitionSignature = signature;
            TypeHandle definition = definitionSignature.GetTypeHandleThrowing(
                module, &context, ClassLoader::DontLoadTypes);
            CorElementType kind;
            IfFailThrow(signature.GetElemType(&kind));
            if (kind != ELEMENT_TYPE_CLASS && kind != ELEMENT_TYPE_VALUETYPE) ThrowHR(COR_E_BADIMAGEFORMAT);
            mdToken token;
            IfFailThrow(signature.GetToken(&token));
            AddTypeToken(module, token);
            ULONG count;
            IfFailThrow(signature.GetData(&count));
            if (count > 4096) ThrowHR(E_INVALIDARG);
            SArray<TypeHandle> arguments;
            bool available = !definition.IsNull();
            for (ULONG i = 0; i < count; i++)
            {
                TypeHandle argument = AddFieldSignature(signature, module, context, depth + 1);
                arguments.Append(argument);
                available &= !argument.IsNull();
            }
            if (available)
                AddFieldLookup(TypeKey(definition.GetModule(), definition.GetCl(),
                    Instantiation(arguments.GetElements(), count)));
        }
        else if (element == ELEMENT_TYPE_SZARRAY || element == ELEMENT_TYPE_ARRAY ||
                 element == ELEMENT_TYPE_PTR || element == ELEMENT_TYPE_BYREF)
        {
            TypeHandle argument = AddFieldSignature(signature, module, context, depth + 1);
            ULONG rank = element == ELEMENT_TYPE_SZARRAY ? 1 : 0;
            if (element == ELEMENT_TYPE_ARRAY)
                IfFailThrow(signature.GetData(&rank));
            if (!argument.IsNull())
                AddFieldLookup(TypeKey(element, argument, rank));
        }
        else if (element == ELEMENT_TYPE_FNPTR)
        {
            ULONG callConv, count;
            IfFailThrow(signature.GetCallingConvInfo(&callConv));
            if ((callConv & IMAGE_CEE_CS_CALLCONV_GENERIC) != 0)
            {
                m_fieldsComplete = false;
            }
            else
            {
                IfFailThrow(signature.GetData(&count));
                if (count >= 4096) ThrowHR(E_INVALIDARG);
                SArray<TypeHandle> arguments;
                bool available = true;
                for (ULONG i = 0; i <= count; i++)
                {
                    CorElementType argumentKind;
                    IfFailThrow(signature.PeekElemType(&argumentKind));
                    if (argumentKind == ELEMENT_TYPE_SENTINEL)
                        IfFailThrow(signature.GetElemType(&argumentKind));
                    TypeHandle argument = AddFieldSignature(signature, module, context, depth + 1);
                    arguments.Append(argument);
                    available &= !argument.IsNull();
                }
                if (available)
                    AddFieldLookup(TypeKey(static_cast<BYTE>(callConv), count, arguments.GetElements()));
            }
        }
        else if (element == ELEMENT_TYPE_PINNED)
        {
            AddFieldSignature(signature, module, context, depth + 1);
        }
        signature = next;
        AddFieldType(type);
        return type;
    }
    bool AddFieldMemory(TypeHandle type, Module* module, mdTypeDef token)
    {
        Add(&g_pFreeObjectMethodTable, sizeof(g_pFreeObjectMethodTable));
        Add(&g_pStringClass, sizeof(g_pStringClass));
        Add(&g_pObjectClass, sizeof(g_pObjectClass));
        // The primitive binder slots are indexed directly by CorElementType.
        auto binder = reinterpret_cast<BYTE*>(&g_CoreLib);
        Add(binder + cdac_data<CoreLibBinder>::Classes, sizeof(TADDR));
        auto classes = *reinterpret_cast<MethodTable***>(binder + cdac_data<CoreLibBinder>::Classes);
        Add(classes, (ELEMENT_TYPE_OBJECT + 1) * sizeof(MethodTable*));
        AddTypeToken(module, token);
        if (type.IsNull())
            type = module->LookupTypeDef(token);
        if (type.IsNull()) return false;
        if (type.IsTypeDesc()) ThrowHR(E_INVALIDARG);
        UINT32 parents = 0;
        for (MethodTable* mt = type.AsMethodTable(); mt != nullptr; mt = mt->GetParentMethodTable())
        {
            if (++parents > 4096) ThrowHR(E_INVALIDARG);
            AddFieldType(TypeHandle(mt));
            AddFieldLayout(mt);
            SigTypeContext context(mt->GetInstantiation(), Instantiation());
            ApproxFieldDescIterator fields(mt, ApproxFieldDescIterator::ALL_FIELDS);
            for (FieldDesc* field = fields.Next(); field != nullptr; field = fields.Next())
            {
                Add(field, sizeof(*field));
                if (field->IsStatic()) continue;
                PCCOR_SIGNATURE bytes;
                DWORD size;
                field->GetSig(&bytes, &size);
                Add(bytes, size);
                SigPointer signature(bytes, size);
                ULONG callConv;
                IfFailThrow(signature.GetCallingConv(&callConv));
                if (callConv != IMAGE_CEE_CS_CALLCONV_FIELD) ThrowHR(COR_E_BADIMAGEFORMAT);
                AddFieldSignature(signature, field->GetModule(), context);
            }
        }
        return m_fieldsComplete;
    }

    void AddCommonMemory()
    {
        _ASSERTE(g_pDebugger->IsStopped());
        Add(cdac_data<AppDomain>::AppDomainAddress, sizeof(PTR_AppDomain));
        Add(&g_cdacPlatformMetadata, sizeof(g_cdacPlatformMetadata));
        AddFunctionTableRanges();
        ThreadStore* store = ThreadStore::s_pThreadStore;
        UINT32 visited = 0;
        for (Thread* thread = store->m_ThreadList.GetHead(); thread != nullptr;
            thread = store->m_ThreadList.GetNext(thread))
        {
            if (++visited > 4096) ThrowHR(E_INVALIDARG);
            // GetThreadData constructs the complete summary, even for an IsThreadMarkedDead query.
            const size_t offsets[] = {
                cdac_data<Thread>::Id, cdac_data<Thread>::OSId, cdac_data<Thread>::State,
                cdac_data<Thread>::PreemptiveGCDisabled, cdac_data<Thread>::RuntimeThreadLocals,
                cdac_data<Thread>::Frame, cdac_data<Thread>::GCFrame,
                cdac_data<Thread>::ExposedObject, cdac_data<Thread>::LastThrownObject,
                cdac_data<Thread>::LastThrownObjectIsUnhandled, cdac_data<Thread>::Link,
                cdac_data<Thread>::CurrentCustomDebuggerNotification, cdac_data<Thread>::ExceptionTracker,
                cdac_data<Thread>::DebuggerFilterContext, cdac_data<Thread>::InteropDebuggingHijacked
            };
            static_assert(sizeof(TADDR) == sizeof(UINT32));
            for (size_t offset : offsets)
                Add(reinterpret_cast<BYTE*>(thread) + offset, sizeof(TADDR));
            ee_alloc_context* alloc = thread->GetEEAllocContext();
            if (alloc != nullptr)
                Add(alloc, sizeof(*alloc));
            ExInfo* exception = thread->GetExceptionState()->GetCurrentExceptionTracker();
            if (exception != nullptr)
                Add(exception, sizeof(*exception));
            T_CONTEXT* context = thread->GetFilterContext();
            if (context != nullptr)
            {
                Add(context, sizeof(*context));
                AddCodeLookup(static_cast<PCODE>(context->InterpreterIP));
            }
        }

        auto assemblies = SystemDomain::System()->DefaultDomain()->IterateAssembliesEx(
            static_cast<AssemblyIterationFlags>(kIncludeLoaded | kIncludeLoading | kIncludeExecution | kIncludeFailedToLoad));
        CollectibleAssemblyHolder<Assembly*> assembly;
        while (assemblies.Next(assembly.This()))
        {
            Add(reinterpret_cast<BYTE*>(assembly.GetValue()) + cdac_data<Assembly>::Module, sizeof(Module*));
            Add(reinterpret_cast<BYTE*>(assembly.GetValue()) + cdac_data<Assembly>::IsLoaded, sizeof(bool));
            Module* module = assembly->GetModule();
            if (module != nullptr)
            {
                BYTE* address = reinterpret_cast<BYTE*>(module);
                Add(address + cdac_data<Module>::Assembly, sizeof(Assembly*));
                Add(address + cdac_data<Module>::Path, sizeof(LPCWSTR));
                Add(address + cdac_data<Module>::SimpleName, sizeof(LPCUTF8));
                LPCWSTR path = *reinterpret_cast<LPCWSTR*>(address + cdac_data<Module>::Path);
                if (path != nullptr)
                    Add(path, (u16_strlen(path) + 1) * sizeof(WCHAR));
                LPCUTF8 name = *reinterpret_cast<LPCUTF8*>(address + cdac_data<Module>::SimpleName);
                if (name != nullptr)
                    Add(name, strlen(name) + 1);
            }
        }
    }
    void AddFunctionTableRanges()
    {
        auto ranges = cdac_data<ExecutionManager>::FunctionTableIndexRangeListAddress;
        Add(ranges, sizeof(*ranges));
        UINT32 rangeCount = 0;
        for (auto range = *ranges; range != nullptr; range = range->pNext)
        {
            if (++rangeCount > 4096) ThrowHR(E_INVALIDARG);
            Add(range, sizeof(*range));
        }
    }
    InterpreterCodeHeader* AddCodeLookup(PCODE ip)
    {
        RangeSectionMap* map = ExecutionManager::GetCodeRangeMap();
        Add(map, sizeof(*map));
        RangeSectionLockState lockState = RangeSectionLockState::ReaderLocked;
        auto level = map->GetTopLevel()[map->EffectiveBitsForLevel(ip, 2)].VolatileLoad(&lockState);
        if (level != nullptr)
            Add(level, sizeof(*level));
        auto fragment = map->GetRangeSectionForAddress(ip, &lockState);
        UINT32 visited = 0;
        while (fragment != nullptr)
        {
            if (++visited > 4096) ThrowHR(E_INVALIDARG);
            Add(fragment, sizeof(*fragment));
            RangeSection* range = fragment->pRangeSection;
            Add(range, sizeof(*range));
            if (fragment->InRange(ip))
            {
                if (range->_pHeapList != nullptr)
                {
                    HeapList* heap = range->_pHeapList;
                    Add(heap, sizeof(*heap));
                    EECodeInfo info(ip);
                    if (info.IsInterpretedCode())
                    {
                        TADDR code = info.GetStartAddress();
                        // Include the complete backwards nibble-map search from the IP to its method.
                        size_t firstWord = ADDR2POS(code - heap->mapBase) / NIBBLES_PER_DWORD;
                        size_t lastWord = ADDR2POS(ip - heap->mapBase) / NIBBLES_PER_DWORD;
                        Add(heap->pHdrMap + firstWord, (lastWord - firstWord + 1) * sizeof(DWORD));
                        InterpreterCodeHeader* header = reinterpret_cast<InterpreterCodeHeader*>(code) - 1;
                        Add(header, sizeof(*header));
                        Add(header->pRealCodeHeader, sizeof(InterpreterRealCodeHeader));
                        return header;
                    }
                }
                return nullptr;
            }
            fragment = fragment->pRangeSectionFragmentNext.VolatileLoadWithoutBarrier(&lockState);
        }
        return nullptr;
    }
    void AddStackCode(PCODE ip)
    {
        InterpreterCodeHeader* header = AddCodeLookup(ip);
        if (header == nullptr)
            return;
        AddStackMethod(header->GetMethodDesc());
        BYTE* debugInfo = header->GetDebugInfo();
        if (debugInfo != nullptr)
            Add(debugInfo, CompressDebugInfo::GetBlobSize(debugInfo));
        BYTE* gcInfo = header->GetGCInfo();
        if (gcInfo != nullptr)
            Add(gcInfo, header->pRealCodeHeader->cbGCInfo);
    }
    void AddStackModule(Module* module)
    {
        for (COUNT_T i = 0; i < m_stackModules.GetCount(); i++)
            if (m_stackModules[i] == module)
                return;
        m_stackModules.Append(module);
        Add(module, sizeof(*module));
        Add(module->GetAssembly(), sizeof(Assembly));
        PEAssembly* assembly = module->GetPEAssembly();
        Add(assembly, sizeof(*assembly));
        if (!assembly->HasPEImage())
            return;
        PEImage* image = assembly->GetPEImage();
        Add(image, sizeof(*image));
        PEImageLayout* layout = image->GetLoadedLayout();
        if (layout == nullptr)
            layout = image->GetFlatLayout();
        if (layout == nullptr)
            return;
        Add(layout, sizeof(*layout));
        // Keep image headers; cDAC loads and caches assembly metadata on demand.
        Add(layout->GetBase(), min(static_cast<COUNT_T>(1024), layout->GetSize()));
        Add(layout->GetCorHeader(), sizeof(IMAGE_COR20_HEADER));
    }
    void AddThreadStack(Thread* thread)
    {
        Add(thread, sizeof(*thread));
        Add(cdac_data<AppDomain>::AppDomainAddress, sizeof(PTR_AppDomain));
        Add(&g_pFreeObjectMethodTable, sizeof(g_pFreeObjectMethodTable));
        Add(&g_cdacPlatformMetadata, sizeof(g_cdacPlatformMetadata));
        AddFunctionTableRanges();
        ee_alloc_context* alloc = thread->GetEEAllocContext();
        if (alloc != nullptr)
            Add(alloc, sizeof(*alloc));
        OBJECTHANDLE handles[] = { thread->GetExposedObjectHandleForDebugger(),
            thread->LastThrownObjectHandle(), thread->GetThreadCurrNotification() };
        for (OBJECTHANDLE handle : handles)
            if (handle != nullptr)
                Add(handle, sizeof(*handle));
        UINT32 exceptionCount = 0;
        for (ExInfo* exception = thread->GetExceptionState()->GetCurrentExceptionTracker();
            exception != nullptr; exception = exception->GetPreviousExceptionTracker())
        {
            if (++exceptionCount > 4096) ThrowHR(E_INVALIDARG);
            Add(exception, sizeof(*exception));
        }
        T_CONTEXT* context = thread->GetFilterContext();
        if (context != nullptr)
            Add(context, sizeof(*context));
        UINT32 visited = 0;
        for (Frame* frame = thread->GetFrame(); frame != FRAME_TOP; frame = frame->Next())
        {
            if (++visited > 4096) ThrowHR(E_INVALIDARG);
            Add(frame, sizeof(*frame));
            if (frame->GetFrameIdentifier() == FrameIdentifier::InlinedCallFrame)
                Add(frame, sizeof(InlinedCallFrame));
            if (frame->GetFrameIdentifier() != FrameIdentifier::InterpreterFrame)
                continue;
            InterpreterFrame* interpreter = static_cast<InterpreterFrame*>(frame);
            Add(interpreter, sizeof(*interpreter));
            TADDR transition = interpreter->GetTransitionBlock();
            if (transition != 0)
                Add(reinterpret_cast<void*>(transition), sizeof(TransitionBlock));
            for (InterpMethodContextFrame* current = interpreter->GetTopInterpMethodContextFrame();
                current != nullptr; current = current->pParent)
            {
                if (++visited > 4096) ThrowHR(E_INVALIDARG);
                Add(current, sizeof(*current));
                if (current->startIp == nullptr)
                    continue;
                Add(current->startIp, sizeof(InterpByteCodeStart));
                InterpMethod* method = current->startIp->Method;
                Add(method, sizeof(*method));
                MethodDesc* md = method->methodHnd;
                AddStackMethod(md);
                Module* module = md->GetModule();
                if (!md->IsDynamicMethod())
                {
                    DWORD rid = RidFromToken(md->GetMemberDef());
                    UINT32 mapCount = 0;
                    for (LookupMapBase* map = module->GetMethodDefToDescMap(); map != nullptr; map = map->pNext)
                    {
                        if (++mapCount > 4096) ThrowHR(E_INVALIDARG);
                        Add(map, sizeof(*map));
                        if (rid < map->dwCount)
                        {
                            Add(map->GetIndexPtr(rid), sizeof(TADDR));
                            break;
                        }
                        rid -= map->dwCount;
                    }
                    MethodDesc* definition = module->LookupMethodDef(md->GetMemberDef());
                    if (definition != nullptr && definition != md)
                        AddStackMethod(definition);
                }
                if (md->AcquiresInstMethodTableFromThis())
                {
                    Object* object = *reinterpret_cast<Object**>(current->pStack);
                    if (object != nullptr)
                        Add(object, sizeof(*object));
                }
                if (method->allocaSize > 0)
                    Add(current->pStack, static_cast<size_t>(method->allocaSize));
                AddStackCode(reinterpret_cast<PCODE>(current->ip != nullptr
                    ? current->ip : current->startIp->GetByteCodes()));
            }
        }
    }
    void AddStackMethod(MethodDesc* md)
    {
        for (COUNT_T i = 0; i < m_stackMethods.GetCount(); i++)
            if (m_stackMethods[i] == md)
                return;
        m_stackMethods.Append(md);
        MethodDescChunk* chunk = md->GetMethodDescChunk();
        Add(chunk, chunk->SizeOf());
        if (md->HasStoredSig() || md->IsAsyncVariantMethod())
        {
            PCCOR_SIGNATURE signature = nullptr;
            DWORD signatureSize = 0;
            md->GetSig(&signature, &signatureSize);
            Add(signature, signatureSize);
        }
        MethodDescCodeData* codeData = md->GetCodeDataIfExists();
        if (codeData != nullptr)
            Add(codeData, sizeof(*codeData));
        PCODE entry = md->GetTemporaryEntryPointIfExists();
        if (entry != 0)
            Add(PortableEntryPoint::ToPortableEntryPoint(entry), sizeof(PortableEntryPoint));
        MethodTable* mt = md->GetMethodTable();
        AddType(TypeHandle(mt), true);
        Add(mt->GetVtableIndirections(), mt->GetNumVtableIndirections() * sizeof(PTR_PCODE));
        Add(md->GetAddrOfSlot(), sizeof(PCODE));
        if (md->IsDynamicMethod())
        {
            const char* name = md->GetName();
            Add(name, strlen(name) + 1);
        }
        else if (md->IsIL())
        {
            COR_ILMETHOD* il = md->GetILHeader();
            if (il != nullptr)
                Add(il, sizeof(COR_ILMETHOD));
        }
        Instantiation inst = md->GetMethodInstantiation();
        Add(inst.GetRawArgs(), inst.GetNumArgs() * sizeof(TypeHandle));
        for (DWORD i = 0; i < inst.GetNumArgs(); i++)
            AddType(inst[i], false);
        AddStackModule(md->GetModule());
    }
    void Copy(BYTE* buffer)
    {
        for (UINT32 i = 0; i < Count(); i++)
        {
            UINT64 address = m_lines[i];
            BYTE* record = buffer + i * DebuggerPrefetchProtocol::RecordSize;
            memcpy(record, &address, sizeof(address));
            memcpy(record + sizeof(address), reinterpret_cast<const void*>(static_cast<size_t>(address)),
                DebuggerPrefetchProtocol::LineSize);
        }
    }
};

HRESULT Debugger::PrefetchMemory(DebuggerRCThread* rcThread, Assembly* assembly, Thread* thread, UINT32 version,
                                bool common)
{
    DebuggerIPCEvent* event = rcThread->GetIPCEventReceiveBuffer();
    InitIPCEvent(event, DB_IPCE_PREFETCH_TYPE_MEMORY_RESULT, nullptr);
    event->PrefetchTypeMemory.version = DebuggerPrefetchProtocol::Version;
    event->PrefetchTypeMemory.lineSize = DebuggerPrefetchProtocol::LineSize;
    HRESULT hr = S_OK;
    NewArrayHolder<BYTE> payload;
    EX_TRY
    {
        if (version != DebuggerPrefetchProtocol::Version || (!common && assembly == nullptr && thread == nullptr))
            ThrowHR(E_INVALIDARG);
        WasmMemoryCollector collector;
        if (common)
        {
            collector.AddCommonMemory();
        }
        else if (thread != nullptr)
        {
            WasmMemoryCollector::ValidateThread(thread);
            collector.AddThreadStack(thread);
        }
        else
        {
            Module* module = assembly->GetModule();
            EETypeHashTable* table = module->GetAvailableParamTypes();
            collector.Add(module, sizeof(Module));
            if (table != nullptr)
            {
                collector.Add(table, sizeof(EETypeHashTable));
                table->VisitDebuggerTypeMemory(collector);
            }
        }
        UINT32 count = collector.Count();
        UINT32 size = count * DebuggerPrefetchProtocol::RecordSize;
        if (count != 0)
        {
            payload = new BYTE[size];
            collector.Copy(payload.GetValue());
        }
        event->PrefetchTypeMemory.lineCount = count;
        event->PrefetchTypeMemory.payloadSize = size;
    }
    EX_CATCH_HRESULT(hr);
    event->hr = hr;
    return rcThread->SendIPCReply(SUCCEEDED(hr) ? payload.GetValue() : nullptr,
        event->PrefetchTypeMemory.payloadSize);
}

HRESULT Debugger::PrefetchFieldMemory(DebuggerRCThread* rcThread, UINT32 version, TypeHandle type,
                                     Module* module, mdTypeDef token)
{
    DebuggerIPCEvent* event = rcThread->GetIPCEventReceiveBuffer();
    InitIPCEvent(event, DB_IPCE_PREFETCH_TYPE_MEMORY_RESULT, nullptr);
    event->PrefetchTypeMemory.version = DebuggerPrefetchProtocol::Version;
    event->PrefetchTypeMemory.lineSize = DebuggerPrefetchProtocol::LineSize;
    HRESULT hr = S_OK;
    NewArrayHolder<BYTE> payload;
    EX_TRY
    {
        if (version != DebuggerPrefetchProtocol::Version || module == nullptr ||
            TypeFromToken(token) != mdtTypeDef || IsNilToken(token))
            ThrowHR(E_INVALIDARG);
        WasmMemoryCollector collector;
        if (!type.IsNull())
            collector.Add(reinterpret_cast<void*>(type.AsTAddr()), sizeof(MethodTable));
        bool complete = collector.AddFieldMemory(type, module, token);
        UINT32 count = collector.Count();
        UINT32 size = count * DebuggerPrefetchProtocol::RecordSize;
        if (size != 0)
        {
            payload = new BYTE[size];
            collector.Copy(payload.GetValue());
        }
        event->PrefetchTypeMemory.lineCount = count;
        event->PrefetchTypeMemory.payloadSize = size;
        hr = complete ? S_OK : S_FALSE;
    }
    EX_CATCH_HRESULT(hr);
    event->hr = hr;
    return rcThread->SendIPCReply(SUCCEEDED(hr) ? payload.GetValue() : nullptr,
        event->PrefetchTypeMemory.payloadSize);
}

HRESULT Debugger::PrefetchEvalResultMemory(DebuggerRCThread* rcThread, UINT32 version, UINT32 flags,
                                         OBJECTHANDLE handle, DebuggerIPCE_ExpandedTypeData resultType)
{
    return PrefetchObjectHandleMemory(rcThread, version, flags, handle, &resultType);
}

HRESULT Debugger::PrefetchHandleDereferenceMemory(DebuggerRCThread* rcThread, UINT32 version, UINT32 flags,
                                                OBJECTHANDLE handle)
{
    return PrefetchObjectHandleMemory(rcThread, version, flags, handle, nullptr);
}

HRESULT Debugger::PrefetchObjectHandleMemory(DebuggerRCThread* rcThread, UINT32 version, UINT32 flags,
                                           OBJECTHANDLE handle, const DebuggerIPCE_ExpandedTypeData* resultType)
{
    DebuggerIPCEvent* event = rcThread->GetIPCEventReceiveBuffer();
    InitIPCEvent(event, DB_IPCE_PREFETCH_TYPE_MEMORY_RESULT, nullptr);
    event->PrefetchTypeMemory.version = DebuggerPrefetchProtocol::Version;
    event->PrefetchTypeMemory.lineSize = DebuggerPrefetchProtocol::LineSize;
    HRESULT hr = S_OK;
    NewArrayHolder<BYTE> payload;
    EX_TRY
    {
        if (handle == nullptr || version != DebuggerPrefetchProtocol::Version ||
            !DebuggerPrefetchProtocol::IsValidHandleRequest(version, flags))
            ThrowHR(E_INVALIDARG);
        WasmMemoryCollector collector;
        if ((flags & DebuggerPrefetchProtocol::HandleCommon) != 0)
            collector.AddCommonMemory();
        UINT32 commonCount = collector.Count();
        collector.AddObjectHandle(handle);
        if (resultType != nullptr)
            collector.AddResultType(*resultType);
        else
            collector.AddObjectDereferenceMemory(handle);
        UINT32 count = collector.Count();
        UINT32 size = count * DebuggerPrefetchProtocol::RecordSize;
        payload = new BYTE[size];
        collector.Copy(payload.GetValue());
        event->PrefetchTypeMemory.lineCount = count;
        event->PrefetchTypeMemory.commonLineCount = commonCount;
        event->PrefetchTypeMemory.payloadSize = size;
    }
    EX_CATCH_HRESULT(hr);
    event->hr = hr;
    return rcThread->SendIPCReply(SUCCEEDED(hr) ? payload.GetValue() : nullptr,
        event->PrefetchTypeMemory.payloadSize);
}

HRESULT Debugger::PrefetchHandleMemory(DebuggerRCThread* rcThread, OBJECTHANDLE handle, UINT32 flags)
{
    DebuggerIPCEvent* event = rcThread->GetIPCEventReceiveBuffer();
    event->CreateHandleResult.version = DebuggerPrefetchProtocol::Version;
    event->CreateHandleResult.lineSize = DebuggerPrefetchProtocol::LineSize;
    HRESULT hr = S_OK;
    NewArrayHolder<BYTE> payload;
    EX_TRY
    {
        WasmMemoryCollector collector;
        if ((flags & DebuggerPrefetchProtocol::HandleCommon) != 0)
            collector.AddCommonMemory();
        UINT32 commonCount = collector.Count();
        collector.AddObjectHandle(handle);
        UINT32 count = collector.Count();
        UINT32 size = count * DebuggerPrefetchProtocol::RecordSize;
        payload = new BYTE[size];
        collector.Copy(payload.GetValue());
        event->CreateHandleResult.lineCount = count;
        event->CreateHandleResult.commonLineCount = commonCount;
        event->CreateHandleResult.payloadSize = size;
    }
    EX_CATCH_HRESULT(hr);
    event->hr = hr;
    if (FAILED(hr))
    {
        DestroyTypedHandle(handle);
        handle = nullptr;
        event->CreateHandleResult.vmObjectHandle = VMPTR_OBJECTHANDLE::NullPtr();
    }
    HRESULT sendResult = rcThread->SendIPCReply(payload.GetValue(), event->CreateHandleResult.payloadSize);
    if (FAILED(sendResult) && handle != nullptr)
        DestroyTypedHandle(handle);
    return sendResult;
}

EXTERN_C const IID IID_IDebugChannel;

EM_JS(int, coreclr_wasm_debugger_transport_available, (), {
    return globalThis.__coreclrWasmDebuggerTransport ? 1 : 0;
});

EM_JS(int, coreclr_wasm_debugger_incoming_byte_count, (), {
    return globalThis.__coreclrWasmDebuggerTransport?.incomingByteCount() ?? -1;
});

EM_JS(int, coreclr_wasm_debugger_read_incoming, (BYTE* buffer, int size), {
    const transport = globalThis.__coreclrWasmDebuggerTransport;
    if (!transport || size < 0 || transport.incomingByteCount() < size) {
        return 0;
    }

    const decoded = atob(transport.dequeueIncoming(size));
    if (decoded.length !== size) {
        return 0;
    }

    for (let i = 0; i < size; i++) {
        HEAPU8[buffer + i] = decoded.charCodeAt(i);
    }
    return 1;
});

EM_JS(int, coreclr_wasm_debugger_write_outgoing, (const BYTE* data, int size), {
    const transport = globalThis.__coreclrWasmDebuggerTransport;
    if (!transport || size < 0) {
        return 0;
    }

    return transport.enqueueOutgoingBytes(HEAPU8.subarray(data, data + size)) ? 1 : 0;
});

class WasmDebuggerChannel final : public IDebugChannel
{
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override
    {
        if (object == nullptr)
        {
            return E_POINTER;
        }

        if (riid == IID_IUnknown || riid == IID_IDebugChannel)
        {
            *object = static_cast<IDebugChannel*>(this);
            AddRef();
            return S_OK;
        }

        *object = nullptr;
        return E_NOINTERFACE;
    }

    ULONG STDMETHODCALLTYPE AddRef() override
    {
        return static_cast<ULONG>(InterlockedIncrement(&m_refCount));
    }

    ULONG STDMETHODCALLTYPE Release() override
    {
        LONG refCount = InterlockedDecrement(&m_refCount);
        if (refCount == 0)
        {
            delete this;
        }
        return static_cast<ULONG>(refCount);
    }

    HRESULT STDMETHODCALLTYPE WaitForConnection(
        DWORD timeoutMilliseconds,
        BOOL* isConnected) override
    {
        if (isConnected == nullptr)
        {
            return E_POINTER;
        }

        *isConnected = coreclr_wasm_debugger_transport_available() ? TRUE : FALSE;
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE CloseConnection() override
    {
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE Read(BYTE* buffer, DWORD size) override
    {
        if (size != 0 && buffer == nullptr)
        {
            return E_POINTER;
        }
        if (size > INT_MAX)
        {
            return E_INVALIDARG;
        }

        return coreclr_wasm_debugger_read_incoming(buffer, static_cast<int>(size)) ? S_OK : E_FAIL;
    }

    HRESULT STDMETHODCALLTYPE Write(BYTE* data, DWORD size) override
    {
        if (size != 0 && data == nullptr)
        {
            return E_POINTER;
        }
        if (size > INT_MAX)
        {
            return E_INVALIDARG;
        }

        return coreclr_wasm_debugger_write_outgoing(data, static_cast<int>(size)) ? S_OK : E_FAIL;
    }

private:
    LONG m_refCount = 1;
};

static DebuggerIPCEvent s_singleThreadedDebuggerIPCEvent;
static DebuggerController* s_singleThreadedDebuggerEventController;
static CONTEXT* s_singleThreadedDebuggerContext;

bool IsSingleThreadedDebuggerAttached()
{
    return g_pDbgTransport != nullptr && g_pDbgTransport->IsOpen();
}

HRESULT InitializeSingleThreadedDebuggerTransport(DebuggerRCThread* rcThread)
{
    WasmDebuggerChannel* channel = new (nothrow) WasmDebuggerChannel();
    if (channel == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    g_pDbgTransport = new (nothrow) DbgTransportSession();
    if (g_pDbgTransport == nullptr)
    {
        channel->Release();
        return E_OUTOFMEMORY;
    }

    HRESULT hr = g_pDbgTransport->InitCooperative(rcThread->GetDCB(), channel);
    channel->Release();
    return hr;
}

DebuggerIPCEvent* GetSingleThreadedDebuggerIPCEventBuffer()
{
    return &s_singleThreadedDebuggerIPCEvent;
}

HRESULT SendSingleThreadedDebuggerIPCEvent(DebuggerIPCEvent* event)
{
    _ASSERTE(event == &s_singleThreadedDebuggerIPCEvent);
    if (g_pDbgTransport == nullptr)
    {
        return E_UNEXPECTED;
    }

    if (event->type == DB_IPCE_SYNC_COMPLETE)
    {
        return g_pDbgTransport->SendEvent(event);
    }

    return g_pDbgTransport->SendDebugEvent(event);
}

void PauseSingleThreadedDebugger()
{
    emscripten_debugger();
}

extern "C" EMSCRIPTEN_KEEPALIVE int CoreClrWasmDebuggerTransportPump()
{
    if (g_pDbgTransport == nullptr || g_pRCThread == nullptr || g_pDebugger == nullptr)
    {
        return E_UNEXPECTED;
    }

    BYTE buffer[4096];
    int available = coreclr_wasm_debugger_incoming_byte_count();
    while (available > 0)
    {
        int readSize = min(available, static_cast<int>(sizeof(buffer)));
        if (!coreclr_wasm_debugger_read_incoming(buffer, readSize))
        {
            return E_FAIL;
        }

        HRESULT hr = g_pDbgTransport->ProcessIncomingBytes(buffer, static_cast<DWORD>(readSize));
        if (FAILED(hr))
        {
            return hr;
        }
        available = coreclr_wasm_debugger_incoming_byte_count();
    }

    if (available < 0)
    {
        return E_UNEXPECTED;
    }

    while (g_pDbgTransport->HasPendingEvent())
    {
        DebuggerIPCEvent* event = g_pRCThread->GetIPCEventReceiveBuffer();
        g_pDbgTransport->GetNextEvent(event, CorDBIPC_BUFFER_SIZE);
        Debugger::DebuggerLockHolder debuggerLock(g_pDebugger, FALSE);
        DebuggerIPCEventType eventType =
            static_cast<DebuggerIPCEventType>(event->type & DB_IPCE_TYPE_MASK);
        if (!g_pDebugger->IsStopped() && eventType == DB_IPCE_CONTINUE)
        {
            g_pDebugger->ResumeThreads(event->vmAppDomain.GetRawPtr());
            return S_FALSE;
        }
        if (g_pDebugger->IsStopped() &&
            eventType != DB_IPCE_ASYNC_BREAK &&
            eventType != DB_IPCE_ATTACHING &&
            !g_pDebugger->ThreadHoldsLock())
        {
            debuggerLock.Acquire();
        }
        bool wasContinue;
        Thread* currentThread = GetThreadNULLOk();
        if (currentThread != nullptr)
        {
            // Browser debugger evaluation re-enters the runtime on the stopped
            // managed thread, but HandleIPCEvent requires cooperative mode.
            GCX_COOP_THREAD_EXISTS(currentThread);
            wasContinue = HandleIPCEventWrapper(g_pDebugger, event);
        }
        else
        {
            wasContinue = HandleIPCEventWrapper(g_pDebugger, event);
        }
        if (wasContinue)
        {
            return S_FALSE;
        }
    }

    return S_OK;
}

void PrepareSingleThreadedDebuggerPatch(Thread* thread, CONTEXT* context, CORDB_ADDRESS_TYPE* address)
{
    s_singleThreadedDebuggerEventController = nullptr;
    s_singleThreadedDebuggerContext = context;

    InterpMethodContextFrame* frame =
        reinterpret_cast<InterpMethodContextFrame*>(GetSP(context));
    if (thread == nullptr ||
        frame == nullptr ||
        frame->startIp == nullptr ||
        frame->startIp->Method == nullptr ||
        frame->startIp->Method->methodHnd == nullptr)
    {
        return;
    }

    DebuggerPatchTable* patches = DebuggerController::GetPatchTable();
    if (patches == nullptr)
    {
        return;
    }

    DebuggerControllerPatch* patch = patches->GetPatch(
        reinterpret_cast<PTR_CORDB_ADDRESS_TYPE>(address));
    if (patch != nullptr)
    {
        s_singleThreadedDebuggerEventController = patch->controller;
    }
}

bool ShouldTriggerSingleThreadedDebuggerBreakpoint(DebuggerBreakpoint* breakpoint)
{
    return breakpoint == s_singleThreadedDebuggerEventController;
}

bool SendSingleThreadedDebuggerBreakpointEvent(Thread* thread, DebuggerBreakpoint* breakpoint)
{
    g_pDebugger->SendBreakpoint(thread, s_singleThreadedDebuggerContext, breakpoint);
    s_singleThreadedDebuggerEventController = nullptr;
    s_singleThreadedDebuggerContext = nullptr;
    return true;
}

bool SendSingleThreadedDebuggerStepperEvent(Thread* thread, DebuggerStepper* stepper)
{
    g_pDebugger->SendStep(
        thread,
        s_singleThreadedDebuggerContext,
        stepper,
        stepper->GetStepReason());
    stepper->Delete();
    s_singleThreadedDebuggerEventController = nullptr;
    s_singleThreadedDebuggerContext = nullptr;
    return true;
}
