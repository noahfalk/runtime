// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

internal sealed class MockModuleData : TypedView
{
    public static Layout<MockModuleData> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("Module", architecture)
            .AddPointerField("Assembly")
            .AddPointerField("PEAssembly")
            .AddPointerField("Base")
            .AddUInt32Field("Flags")
            .AddPointerField("LoaderAllocator")
            .AddPointerField("DynamicMetadata")
            .AddPointerField("Path")
            .AddPointerField("FileName")
            .AddPointerField("ReadyToRunInfo")
            .AddPointerField("GrowableSymbolStream")
            .AddPointerField("AvailableTypeParams")
            .AddPointerField("InstMethodHashTable")
            .AddPointerField("FieldDefToDescMap")
            .AddPointerField("ManifestModuleReferencesMap")
            .AddPointerField("MemberRefToDescMap")
            .AddPointerField("MethodDefToDescMap")
            .AddPointerField("TypeDefToMethodTableMap")
            .AddPointerField("TypeRefToMethodTableMap")
            .AddPointerField("MethodDefToILCodeVersioningStateMap")
            .AddPointerField("DynamicILBlobTable")
            .Build<MockModuleData>();

    public ulong Assembly
    {
        get => ReadPointerField("Assembly");
        set => WritePointerField("Assembly", value);
    }

    public ulong Path
    {
        get => ReadPointerField("Path");
        set => WritePointerField("Path", value);
    }

    public ulong FileName
    {
        get => ReadPointerField("FileName");
        set => WritePointerField("FileName", value);
    }
}

internal sealed class MockAssemblyData : TypedView
{
    public static Layout<MockAssemblyData> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("Assembly", architecture)
            .AddPointerField("Module")
            .AddField("IsCollectible", sizeof(byte))
            .AddField("IsDynamic", sizeof(byte))
            .AddPointerField("Error")
            .AddUInt32Field("NotifyFlags")
            .AddField("IsLoaded", sizeof(byte))
            .Build<MockAssemblyData>();

    public ulong Module
    {
        get => ReadPointerField("Module");
        set => WritePointerField("Module", value);
    }
}

internal sealed class MockProbeExtensionResult : TypedView
{
    public static Layout<MockProbeExtensionResult> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("ProbeExtensionResult", architecture)
            .AddInt32Field("Type")
            .Build<MockProbeExtensionResult>();

    public int Type
    {
        get => ReadInt32Field("Type");
        set => WriteInt32Field("Type", value);
    }
}

internal sealed class MockPEAssemblyData : TypedView
{
    public static Layout<MockPEAssemblyData> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("PEAssembly", architecture)
            .AddPointerField("PEImage")
            .AddPointerField("AssemblyBinder")
            .Build<MockPEAssemblyData>();

    public ulong PEImage
    {
        get => ReadPointerField("PEImage");
        set => WritePointerField("PEImage", value);
    }

    public ulong AssemblyBinder
    {
        get => ReadPointerField("AssemblyBinder");
        set => WritePointerField("AssemblyBinder", value);
    }
}

internal sealed class MockPEImageData : TypedView
{
    public static Layout<MockPEImageData> CreateLayout(MockTarget.Architecture architecture, Layout<MockProbeExtensionResult> probeExtensionResultLayout)
        => new SequentialLayoutBuilder("PEImage", architecture)
            .AddPointerField("LoadedImageLayout")
            .AddField("ProbeExtensionResult", probeExtensionResultLayout.Size, probeExtensionResultLayout)
            .Build<MockPEImageData>();

    public ulong LoadedImageLayout
    {
        get => ReadPointerField("LoadedImageLayout");
        set => WritePointerField("LoadedImageLayout", value);
    }

    public MockProbeExtensionResult ProbeExtensionResult
        => CreateFieldView<MockProbeExtensionResult>("ProbeExtensionResult");
}

internal sealed class MockPEImageLayoutData : TypedView
{
    public static Layout<MockPEImageLayoutData> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("PEImageLayout", architecture)
            .AddPointerField("Base")
            .AddUInt32Field("Size")
            .AddUInt32Field("Flags")
            .AddUInt32Field("Format")
            .Build<MockPEImageLayoutData>();

    public ulong Base
    {
        get => ReadPointerField("Base");
        set => WritePointerField("Base", value);
    }

    public uint Size
    {
        get => ReadUInt32Field("Size");
        set => WriteUInt32Field("Size", value);
    }

    public uint Flags
    {
        get => ReadUInt32Field("Flags");
        set => WriteUInt32Field("Flags", value);
    }

    public uint Format
    {
        get => ReadUInt32Field("Format");
        set => WriteUInt32Field("Format", value);
    }
}

internal sealed class MockVirtualCallStubManager : TypedView
{
    public static Layout<MockVirtualCallStubManager> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("VirtualCallStubManager", architecture)
            .AddPointerField("IndcellHeap")
            .AddPointerField("CacheEntryHeap")
            .Build<MockVirtualCallStubManager>();

    public ulong IndcellHeap
    {
        get => ReadPointerField("IndcellHeap");
        set => WritePointerField("IndcellHeap", value);
    }

    public ulong CacheEntryHeap
    {
        get => ReadPointerField("CacheEntryHeap");
        set => WritePointerField("CacheEntryHeap", value);
    }
}

internal sealed class MockLoaderAllocatorData : TypedView
{
    public static Layout<MockLoaderAllocatorData> CreateLayout(MockTarget.Architecture architecture)
        => new SequentialLayoutBuilder("LoaderAllocator", architecture)
            .AddUInt32Field("ReferenceCount")
            .AddPointerField("HighFrequencyHeap")
            .AddPointerField("LowFrequencyHeap")
            .AddPointerField("StaticsHeap")
            .AddPointerField("StubHeap")
            .AddPointerField("ExecutableHeap")
            .AddPointerField("FixupPrecodeHeap")
            .AddPointerField("NewStubPrecodeHeap")
            .AddPointerField("VirtualCallStubManager")
            .AddPointerField("ObjectHandle")
            .Build<MockLoaderAllocatorData>();

    public uint ReferenceCount
    {
        get => ReadUInt32Field("ReferenceCount");
        set => WriteUInt32Field("ReferenceCount", value);
    }

    public ulong HighFrequencyHeap
    {
        get => ReadPointerField("HighFrequencyHeap");
        set => WritePointerField("HighFrequencyHeap", value);
    }

    public ulong LowFrequencyHeap
    {
        get => ReadPointerField("LowFrequencyHeap");
        set => WritePointerField("LowFrequencyHeap", value);
    }

    public ulong StaticsHeap
    {
        get => ReadPointerField("StaticsHeap");
        set => WritePointerField("StaticsHeap", value);
    }

    public ulong StubHeap
    {
        get => ReadPointerField("StubHeap");
        set => WritePointerField("StubHeap", value);
    }

    public ulong ExecutableHeap
    {
        get => ReadPointerField("ExecutableHeap");
        set => WritePointerField("ExecutableHeap", value);
    }

    public ulong FixupPrecodeHeap
    {
        get => ReadPointerField("FixupPrecodeHeap");
        set => WritePointerField("FixupPrecodeHeap", value);
    }

    public ulong NewStubPrecodeHeap
    {
        get => ReadPointerField("NewStubPrecodeHeap");
        set => WritePointerField("NewStubPrecodeHeap", value);
    }

    public ulong VirtualCallStubManager
    {
        get => ReadPointerField("VirtualCallStubManager");
        set => WritePointerField("VirtualCallStubManager", value);
    }
}

internal sealed class MockSystemDomain : TypedView
{
    public static Layout<MockSystemDomain> CreateLayout(MockTarget.Architecture architecture, Layout<MockLoaderAllocatorData> loaderAllocatorLayout)
        => new SequentialLayoutBuilder("SystemDomain", architecture)
            .AddField("GlobalLoaderAllocator", loaderAllocatorLayout.Size, loaderAllocatorLayout)
            .AddPointerField("SystemAssembly")
            .Build<MockSystemDomain>();

    public MockLoaderAllocatorData GlobalLoaderAllocator
        => CreateFieldView<MockLoaderAllocatorData>("GlobalLoaderAllocator");
}

public sealed class MockLoaderBuilder
{
    private readonly MockMemorySpace.BumpAllocator _allocator;
    private readonly MockTarget.Architecture _architecture;
    private readonly MockSystemDomain _systemDomain;

    internal MockLoaderBuilder(
        MockMemorySpace.BumpAllocator allocator,
        MockTarget.Architecture architecture)
    {
        _allocator = allocator;
        _architecture = architecture;

        ModuleLayout = MockModuleData.CreateLayout(architecture);
        AssemblyLayout = MockAssemblyData.CreateLayout(architecture);
        ProbeExtensionResultLayout = MockProbeExtensionResult.CreateLayout(architecture);
        PEAssemblyLayout = MockPEAssemblyData.CreateLayout(architecture);
        PEImageLayout = MockPEImageData.CreateLayout(architecture, ProbeExtensionResultLayout);
        PEImageLayoutDataLayout = MockPEImageLayoutData.CreateLayout(architecture);
        LoaderAllocatorLayout = MockLoaderAllocatorData.CreateLayout(architecture);
        VirtualCallStubManagerLayout = MockVirtualCallStubManager.CreateLayout(architecture);
        SystemDomainLayout = MockSystemDomain.CreateLayout(architecture, LoaderAllocatorLayout);

        _systemDomain = SystemDomainLayout.Allocate(_allocator, "SystemDomain");
        SystemDomainGlobalAddress = _allocator.AllocatePointer(_systemDomain.Address, "[global pointer] SystemDomain");
    }

    public ulong SystemDomainGlobalAddress { get; }

    public ulong GlobalLoaderAllocatorAddress => _systemDomain.GlobalLoaderAllocator.Address;

    internal Layout<MockModuleData> ModuleLayout { get; }

    internal Layout<MockAssemblyData> AssemblyLayout { get; }

    internal Layout<MockProbeExtensionResult> ProbeExtensionResultLayout { get; }

    internal Layout<MockPEAssemblyData> PEAssemblyLayout { get; }

    internal Layout<MockPEImageData> PEImageLayout { get; }

    internal Layout<MockPEImageLayoutData> PEImageLayoutDataLayout { get; }

    internal Layout<MockLoaderAllocatorData> LoaderAllocatorLayout { get; }

    internal Layout<MockVirtualCallStubManager> VirtualCallStubManagerLayout { get; }

    internal Layout<MockSystemDomain> SystemDomainLayout { get; }

    public ulong AddModule(string? path = null, string? fileName = null)
    {
        MockModuleData module = ModuleLayout.Allocate(_allocator, "Module");
        MockAssemblyData assembly = AssemblyLayout.Allocate(_allocator, "Assembly");
        module.Assembly = assembly.Address;
        assembly.Module = module.Address;

        if (path is not null)
        {
            module.Path = AllocateUtf16String(path, $"Module path = {path}");
        }

        if (fileName is not null)
        {
            module.FileName = AllocateUtf16String(fileName, $"Module file name = {fileName}");
        }

        return module.Address;
    }

    public ulong AddPEAssembly(ulong peImageAddress, ulong assemblyBinderAddress = 0)
    {
        MockPEAssemblyData peAssembly = PEAssemblyLayout.Allocate(_allocator, "PEAssembly");
        peAssembly.PEImage = peImageAddress;

        if (assemblyBinderAddress != 0)
        {
            peAssembly.AssemblyBinder = assemblyBinderAddress;
        }

        return peAssembly.Address;
    }

    public ulong AddPEImage(ulong loadedImageLayoutAddress, int probeExtensionResultType = 0)
    {
        MockPEImageData peImage = PEImageLayout.Allocate(_allocator, "PEImage");
        peImage.LoadedImageLayout = loadedImageLayoutAddress;
        peImage.ProbeExtensionResult.Type = probeExtensionResultType;
        return peImage.Address;
    }

    public ulong AddPEImageLayout(ulong imageBaseAddress, uint size, uint flags = 0, uint format = 0)
    {
        MockPEImageLayoutData imageLayout = PEImageLayoutDataLayout.Allocate(_allocator, "PEImageLayout");
        imageLayout.Base = imageBaseAddress;
        imageLayout.Size = size;
        imageLayout.Flags = flags;
        imageLayout.Format = format;
        return imageLayout.Address;
    }

    public ulong SetGlobalLoaderAllocatorHeaps(IReadOnlyDictionary<string, ulong> heaps)
    {
        MockLoaderAllocatorData loaderAllocator = _systemDomain.GlobalLoaderAllocator;
        loaderAllocator.ReferenceCount = 1;

        if (heaps.TryGetValue("LowFrequencyHeap", out ulong lowFrequencyHeap))
        {
            loaderAllocator.LowFrequencyHeap = lowFrequencyHeap;
        }

        if (heaps.TryGetValue("HighFrequencyHeap", out ulong highFrequencyHeap))
        {
            loaderAllocator.HighFrequencyHeap = highFrequencyHeap;
        }

        if (heaps.TryGetValue("StaticsHeap", out ulong staticsHeap))
        {
            loaderAllocator.StaticsHeap = staticsHeap;
        }

        if (heaps.TryGetValue("StubHeap", out ulong stubHeap))
        {
            loaderAllocator.StubHeap = stubHeap;
        }

        if (heaps.TryGetValue("ExecutableHeap", out ulong executableHeap))
        {
            loaderAllocator.ExecutableHeap = executableHeap;
        }

        if (heaps.TryGetValue("FixupPrecodeHeap", out ulong fixupPrecodeHeap))
        {
            loaderAllocator.FixupPrecodeHeap = fixupPrecodeHeap;
        }

        if (heaps.TryGetValue("NewStubPrecodeHeap", out ulong newStubPrecodeHeap))
        {
            loaderAllocator.NewStubPrecodeHeap = newStubPrecodeHeap;
        }

        heaps.TryGetValue("IndcellHeap", out ulong indcellHeap);
        heaps.TryGetValue("CacheEntryHeap", out ulong cacheEntryHeap);
        if (indcellHeap != 0 || cacheEntryHeap != 0)
        {
            MockVirtualCallStubManager virtualCallStubManager = VirtualCallStubManagerLayout.Allocate(_allocator, "VirtualCallStubManager");
            if (indcellHeap != 0)
            {
                virtualCallStubManager.IndcellHeap = indcellHeap;
            }

            if (cacheEntryHeap != 0)
            {
                virtualCallStubManager.CacheEntryHeap = cacheEntryHeap;
            }

            loaderAllocator.VirtualCallStubManager = virtualCallStubManager.Address;
        }

        return loaderAllocator.Address;
    }

    private ulong AllocateUtf16String(string value, string name)
    {
        Encoding encoding = _architecture.IsLittleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        byte[] encoded = encoding.GetBytes(value);
        MockMemorySpace.HeapFragment fragment = _allocator.AllocateFragment((ulong)(encoded.Length + sizeof(char)), name);
        encoded.CopyTo(fragment.Data, 0);
        return fragment.Address;
    }
}

public static class MockLoaderBuilderExtensions
{
    private const string LoaderContractName = "Loader";

    public static MockProcessBuilder AddLoader(
        this MockProcessBuilder processBuilder,
        Action<MockLoaderBuilder> configure)
    {
        MockLoaderBuilder config = new(
            processBuilder.MemoryBuilder.DefaultAllocator,
            processBuilder.Architecture);
        configure(config);

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptor =>
            {
                descriptor.AddType(config.ModuleLayout);
                descriptor.AddType(config.AssemblyLayout);
                descriptor.AddType(config.ProbeExtensionResultLayout);
                descriptor.AddType(config.PEAssemblyLayout);
                descriptor.AddType(config.PEImageLayout);
                descriptor.AddType(config.PEImageLayoutDataLayout);
                descriptor.AddType(config.LoaderAllocatorLayout);
                descriptor.AddType(config.VirtualCallStubManagerLayout);
                descriptor.AddType(config.SystemDomainLayout);
                descriptor
                    .AddContract(LoaderContractName, 1)
                    .AddGlobalValue("SystemDomain", config.SystemDomainGlobalAddress);
            });
        });

        return processBuilder;
    }
}
