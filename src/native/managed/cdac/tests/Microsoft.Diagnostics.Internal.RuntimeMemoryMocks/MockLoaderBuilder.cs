// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

internal sealed record LoaderDescriptorTypes(
    MockDataDescriptorType Module,
    MockDataDescriptorType Assembly,
    MockDataDescriptorType ProbeExtensionResult,
    MockDataDescriptorType PEAssembly,
    MockDataDescriptorType PEImage,
    MockDataDescriptorType PEImageLayout,
    MockDataDescriptorType LoaderAllocator,
    MockDataDescriptorType VirtualCallStubManager,
    MockDataDescriptorType SystemDomain);

public sealed class MockLoaderBuilder
{
    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly MockTarget.Architecture _architecture;
    private readonly LoaderDescriptorTypes _types;

    private readonly ulong _systemDomainAddress;

    internal MockLoaderBuilder(
        MockMemorySpace.BumpAllocator allocator,
        MockTarget.Architecture architecture,
        LoaderDescriptorTypes types)
    {
        _allocator = allocator;
        _architecture = architecture;
        _types = types;

        _systemDomainAddress = _allocator.AllocateFragment((ulong)GetRequiredSize(_types.SystemDomain), "SystemDomain").Address;
        SystemDomainGlobalAddress = _allocator.AllocatePointer(_systemDomainAddress, "[global pointer] SystemDomain");
    }

    public ulong SystemDomainGlobalAddress { get; }
    public ulong GlobalLoaderAllocatorAddress => _systemDomainAddress + (ulong)_types.SystemDomain.GetFieldOffset("GlobalLoaderAllocator");

    public ulong AddModule(string? path = null, string? fileName = null)
    {
        MockMemorySpace.HeapFragment moduleFragment = _allocator.AllocateFragment((ulong)GetRequiredSize(_types.Module), "Module");
        MockMemorySpace.HeapFragment assemblyFragment = _allocator.AllocateFragment((ulong)GetRequiredSize(_types.Assembly), "Assembly");

        FieldWriter moduleWriter = new(moduleFragment.Data, _architecture, _types.Module);
        FieldWriter assemblyWriter = new(assemblyFragment.Data, _architecture, _types.Assembly);
        moduleWriter.WritePointerField("Assembly", assemblyFragment.Address);
        assemblyWriter.WritePointerField("Module", moduleFragment.Address);

        if (path is not null)
        {
            moduleWriter.WritePointerField("Path", AllocateUtf16String(path, $"Module path = {path}"));
        }

        if (fileName is not null)
        {
            moduleWriter.WritePointerField("FileName", AllocateUtf16String(fileName, $"Module file name = {fileName}"));
        }

        return moduleFragment.Address;
    }

    public ulong AddPEAssembly(ulong peImageAddress, ulong assemblyBinderAddress = 0)
    {
        MockMemorySpace.HeapFragment peAssemblyFragment = _allocator.AllocateFragment((ulong)GetRequiredSize(_types.PEAssembly), "PEAssembly");
        FieldWriter peAssemblyWriter = new(peAssemblyFragment.Data, _architecture, _types.PEAssembly);

        peAssemblyWriter.WritePointerField("PEImage", peImageAddress);
        if (assemblyBinderAddress != 0)
        {
            peAssemblyWriter.WritePointerField("AssemblyBinder", assemblyBinderAddress);
        }

        return peAssemblyFragment.Address;
    }

    public ulong AddPEImage(ulong loadedImageLayoutAddress, int probeExtensionResultType = 0)
    {
        MockMemorySpace.HeapFragment peImageFragment = _allocator.AllocateFragment((ulong)GetRequiredSize(_types.PEImage), "PEImage");
        FieldWriter peImageWriter = new(peImageFragment.Data, _architecture, _types.PEImage);
        FieldWriter probeExtensionResultWriter = new(peImageWriter.GetFieldSlice("ProbeExtensionResult"), _architecture, _types.ProbeExtensionResult);

        peImageWriter.WritePointerField("LoadedImageLayout", loadedImageLayoutAddress);
        probeExtensionResultWriter.WriteInt32Field("Type", probeExtensionResultType);

        return peImageFragment.Address;
    }

    public ulong AddPEImageLayout(ulong imageBaseAddress, uint size, uint flags = 0, uint format = 0)
    {
        MockMemorySpace.HeapFragment imageLayoutFragment = _allocator.AllocateFragment((ulong)GetRequiredSize(_types.PEImageLayout), "PEImageLayout");
        FieldWriter imageLayoutWriter = new(imageLayoutFragment.Data, _architecture, _types.PEImageLayout);

        imageLayoutWriter.WritePointerField("Base", imageBaseAddress);
        imageLayoutWriter.WriteUInt32Field("Size", size);
        imageLayoutWriter.WriteUInt32Field("Flags", flags);
        imageLayoutWriter.WriteUInt32Field("Format", format);

        return imageLayoutFragment.Address;
    }

    public ulong SetGlobalLoaderAllocatorHeaps(IReadOnlyDictionary<string, ulong> heaps)
    {
        Span<byte> loaderAllocatorData = BorrowAddressRange(GlobalLoaderAllocatorAddress, GetRequiredSize(_types.LoaderAllocator));
        FieldWriter loaderAllocatorWriter = new(loaderAllocatorData, _architecture, _types.LoaderAllocator);
        loaderAllocatorWriter.WriteUInt32Field("ReferenceCount", 1);
        WritePointerIfPresent(loaderAllocatorWriter, "LowFrequencyHeap", heaps);
        WritePointerIfPresent(loaderAllocatorWriter, "HighFrequencyHeap", heaps);
        WritePointerIfPresent(loaderAllocatorWriter, "StaticsHeap", heaps);
        WritePointerIfPresent(loaderAllocatorWriter, "StubHeap", heaps);
        WritePointerIfPresent(loaderAllocatorWriter, "ExecutableHeap", heaps);
        WritePointerIfPresent(loaderAllocatorWriter, "FixupPrecodeHeap", heaps);
        WritePointerIfPresent(loaderAllocatorWriter, "NewStubPrecodeHeap", heaps);

        heaps.TryGetValue("IndcellHeap", out ulong indcellHeap);
        heaps.TryGetValue("CacheEntryHeap", out ulong cacheEntryHeap);
        if (indcellHeap != 0 || cacheEntryHeap != 0)
        {
            MockMemorySpace.HeapFragment virtualCallStubManager = _allocator.AllocateFragment((ulong)GetRequiredSize(_types.VirtualCallStubManager), "VirtualCallStubManager");
            FieldWriter virtualCallStubManagerWriter = new(virtualCallStubManager.Data, _architecture, _types.VirtualCallStubManager);
            if (indcellHeap != 0)
            {
                virtualCallStubManagerWriter.WritePointerField("IndcellHeap", indcellHeap);
            }

            if (cacheEntryHeap != 0)
            {
                virtualCallStubManagerWriter.WritePointerField("CacheEntryHeap", cacheEntryHeap);
            }

            loaderAllocatorWriter.WritePointerField("VirtualCallStubManager", virtualCallStubManager.Address);
        }

        return GlobalLoaderAllocatorAddress;
    }

    private ulong AllocateUtf16String(string value, string name)
    {
        Encoding encoding = _architecture.IsLittleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        byte[] encoded = encoding.GetBytes(value);
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment((ulong)(encoded.Length + sizeof(char)), name);
        encoded.CopyTo(fragment.Data, 0);
        return fragment.Address;
    }

    private static int GetRequiredSize(MockDataDescriptorType type)
        => checked((int)(type.Size ?? throw new InvalidOperationException("Expected descriptor type size to be populated.")));

    private Span<byte> BorrowAddressRange(ulong address, int length)
    {
        foreach (MockMemorySpace.HeapFragment fragment in _allocator.Allocations)
        {
            if (address >= fragment.Address && address + (ulong)length <= fragment.Address + (ulong)fragment.Data.Length)
            {
                return fragment.Data.AsSpan((int)(address - fragment.Address), length);
            }
        }

        throw new InvalidOperationException($"No tracked fragment includes addresses from 0x{address:x} with length {length}.");
    }

    private static void WritePointerIfPresent(
        FieldWriter writer,
        string fieldName,
        IReadOnlyDictionary<string, ulong> values)
    {
        if (values.TryGetValue(fieldName, out ulong value))
        {
            writer.WritePointerField(fieldName, value);
        }
    }

    private static LoaderDescriptorTypes AddTypes(MockDataDescriptorBuilder descriptor)
    {
        MockDataDescriptorType module = descriptor.AddSequentialType("Module", type =>
        {
            type.AddPointerField("Assembly");
            type.AddPointerField("PEAssembly");
            type.AddPointerField("Base");
            type.AddUInt32Field("Flags");
            type.AddPointerField("LoaderAllocator");
            type.AddPointerField("DynamicMetadata");
            type.AddPointerField("Path");
            type.AddPointerField("FileName");
            type.AddPointerField("ReadyToRunInfo");
            type.AddPointerField("GrowableSymbolStream");
            type.AddPointerField("AvailableTypeParams");
            type.AddPointerField("InstMethodHashTable");
            type.AddPointerField("FieldDefToDescMap");
            type.AddPointerField("ManifestModuleReferencesMap");
            type.AddPointerField("MemberRefToDescMap");
            type.AddPointerField("MethodDefToDescMap");
            type.AddPointerField("TypeDefToMethodTableMap");
            type.AddPointerField("TypeRefToMethodTableMap");
            type.AddPointerField("MethodDefToILCodeVersioningStateMap");
            type.AddPointerField("DynamicILBlobTable");
        });

        MockDataDescriptorType assembly = descriptor.AddSequentialType("Assembly", type =>
        {
            type.AddPointerField("Module");
            type.AddField("IsCollectible", sizeof(byte));
            type.AddField("IsDynamic", sizeof(byte));
            type.AddPointerField("Error");
            type.AddUInt32Field("NotifyFlags");
            type.AddField("IsLoaded", sizeof(byte));
        });

        MockDataDescriptorType probeExtensionResult = descriptor.AddSequentialType("ProbeExtensionResult", type =>
        {
            type.AddInt32Field("Type");
        });

        MockDataDescriptorType peAssembly = descriptor.AddSequentialType("PEAssembly", type =>
        {
            type.AddPointerField("PEImage");
            type.AddPointerField("AssemblyBinder");
        });

        MockDataDescriptorType peImage = descriptor.AddSequentialType("PEImage", type =>
        {
            type.AddPointerField("LoadedImageLayout");
            type.AddField("ProbeExtensionResult", GetRequiredSize(probeExtensionResult), "ProbeExtensionResult");
        });

        MockDataDescriptorType peImageLayout = descriptor.AddSequentialType("PEImageLayout", type =>
        {
            type.AddPointerField("Base");
            type.AddUInt32Field("Size");
            type.AddUInt32Field("Flags");
            type.AddUInt32Field("Format");
        });

        MockDataDescriptorType loaderAllocator = descriptor.AddSequentialType("LoaderAllocator", type =>
        {
            type.AddUInt32Field("ReferenceCount");
            type.AddPointerField("HighFrequencyHeap");
            type.AddPointerField("LowFrequencyHeap");
            type.AddPointerField("StaticsHeap");
            type.AddPointerField("StubHeap");
            type.AddPointerField("ExecutableHeap");
            type.AddPointerField("FixupPrecodeHeap");
            type.AddPointerField("NewStubPrecodeHeap");
            type.AddPointerField("VirtualCallStubManager");
            type.AddPointerField("ObjectHandle");
        });

        MockDataDescriptorType virtualCallStubManager = descriptor.AddSequentialType("VirtualCallStubManager", type =>
        {
            type.AddPointerField("IndcellHeap");
            type.AddPointerField("CacheEntryHeap");
        });

        MockDataDescriptorType systemDomain = descriptor.AddSequentialType("SystemDomain", type =>
        {
            type.AddField("GlobalLoaderAllocator", GetRequiredSize(loaderAllocator), "LoaderAllocator");
            type.AddPointerField("SystemAssembly");
        });

        return new LoaderDescriptorTypes(module, assembly, probeExtensionResult, peAssembly, peImage, peImageLayout, loaderAllocator, virtualCallStubManager, systemDomain);
    }

    internal static LoaderDescriptorTypes AddDescriptorTypes(MockDataDescriptorBuilder descriptor)
        => AddTypes(descriptor);
}

public static class MockLoaderBuilderExtensions
{
    private const string LoaderContractName = "Loader";

    public static MockProcessBuilder AddLoader(
        this MockProcessBuilder processBuilder,
        Action<MockLoaderBuilder> configure)
    {
        MockMemorySpace.BumpAllocator allocator = processBuilder.MemoryBuilder.DefaultAllocator;
        LoaderDescriptorTypes? types = null;

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptor =>
            {
                types = MockLoaderBuilder.AddDescriptorTypes(descriptor);
                descriptor.AddContract(LoaderContractName, 1);
            });
        });

        MockLoaderBuilder config = new(
            allocator,
            processBuilder.Architecture,
            types ?? throw new InvalidOperationException("Expected loader descriptor types to be initialized."));
        configure(config);

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptor =>
            {
                descriptor.AddGlobalValue("SystemDomain", config.SystemDomainGlobalAddress);
            });
        });

        return processBuilder;
    }
}
