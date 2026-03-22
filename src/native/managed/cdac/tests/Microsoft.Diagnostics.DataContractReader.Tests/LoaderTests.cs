// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DataContractReader;
using Microsoft.Diagnostics.DataContractReader.Contracts;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using Xunit;

namespace Microsoft.Diagnostics.DataContractReader.Tests;

using Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public unsafe class LoaderTests
{
    private static ILoader CreateLoaderContract(
        MockTarget.Architecture arch,
        Action<MockLoaderBuilder> configure)
    {
        MockProcess process = new MockProcessBuilder(arch)
            .AddLoader(configure)
            .Build();

        return process.CreateContractDescriptorTarget().Contracts.Loader;
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetPath(MockTarget.Architecture arch)
    {
        string expected = $"{AppContext.BaseDirectory}{Path.DirectorySeparatorChar}TestModule.dll";
        ulong moduleAddr = 0;
        ulong moduleAddrEmptyPath = 0;
        ILoader contract = CreateLoaderContract(arch, loader =>
        {
            moduleAddr = loader.AddModule(path: expected);
            moduleAddrEmptyPath = loader.AddModule();
        });

        Assert.NotNull(contract);
        {
            Contracts.ModuleHandle handle = contract.GetModuleHandleFromModulePtr(new TargetPointer(moduleAddr));
            string actual = contract.GetPath(handle);
            Assert.Equal(expected, actual);
        }
        {
            Contracts.ModuleHandle handle = contract.GetModuleHandleFromModulePtr(new TargetPointer(moduleAddrEmptyPath));
            string actual = contract.GetFileName(handle);
            Assert.Equal(string.Empty, actual);
        }
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetFileName(MockTarget.Architecture arch)
    {
        string expected = $"TestModule.dll";
        ulong moduleAddr = 0;
        ulong moduleAddrEmptyName = 0;
        ILoader contract = CreateLoaderContract(arch, loader =>
        {
            moduleAddr = loader.AddModule(fileName: expected);
            moduleAddrEmptyName = loader.AddModule();
        });

        Assert.NotNull(contract);
        {
            Contracts.ModuleHandle handle = contract.GetModuleHandleFromModulePtr(new TargetPointer(moduleAddr));
            string actual = contract.GetFileName(handle);
            Assert.Equal(expected, actual);
        }
        {
            Contracts.ModuleHandle handle = contract.GetModuleHandleFromModulePtr(new TargetPointer(moduleAddrEmptyName));
            string actual = contract.GetFileName(handle);
            Assert.Equal(string.Empty, actual);
        }
    }

    private static readonly Dictionary<string, TargetPointer> MockHeapDictionary = new()
    {
        ["LowFrequencyHeap"] = new(0x1000),
        ["HighFrequencyHeap"] = new(0x2000),
        ["StaticsHeap"] = new(0x3000),
        ["StubHeap"] = new(0x4000),
        ["ExecutableHeap"] = new(0x5000),
        ["FixupPrecodeHeap"] = new(0x6000),
        ["NewStubPrecodeHeap"] = new(0x7000),
        ["IndcellHeap"] = new(0x8000),
        ["CacheEntryHeap"] = new(0x9000),
    };

    private static (SOSDacImpl Impl, ClrDataAddress LoaderAllocatorAddress) CreateSOSDacImplForHeapTests(MockTarget.Architecture arch)
    {
        ulong loaderAllocatorAddress = 0;
        MockProcess process = new MockProcessBuilder(arch)
            .AddLoader(loader =>
            {
                loaderAllocatorAddress = loader.SetGlobalLoaderAllocatorHeaps(new Dictionary<string, ulong>
                {
                    ["LowFrequencyHeap"] = MockHeapDictionary["LowFrequencyHeap"],
                    ["HighFrequencyHeap"] = MockHeapDictionary["HighFrequencyHeap"],
                    ["StaticsHeap"] = MockHeapDictionary["StaticsHeap"],
                    ["StubHeap"] = MockHeapDictionary["StubHeap"],
                    ["ExecutableHeap"] = MockHeapDictionary["ExecutableHeap"],
                    ["FixupPrecodeHeap"] = MockHeapDictionary["FixupPrecodeHeap"],
                    ["NewStubPrecodeHeap"] = MockHeapDictionary["NewStubPrecodeHeap"],
                    ["IndcellHeap"] = MockHeapDictionary["IndcellHeap"],
                    ["CacheEntryHeap"] = MockHeapDictionary["CacheEntryHeap"],
                });
            })
            .Build();

        Target target = process.CreateContractDescriptorTarget();
        return (new SOSDacImpl(target, null), new ClrDataAddress(loaderAllocatorAddress));
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetLoaderAllocatorHeapNames_GetCount(MockTarget.Architecture arch)
    {
        (ISOSDacInterface13 impl, _) = CreateSOSDacImplForHeapTests(arch);

        int needed;
        int hr = impl.GetLoaderAllocatorHeapNames(0, null, &needed);

        Assert.Equal(HResults.S_FALSE, hr);
        Assert.Equal(MockHeapDictionary.Count, needed);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetLoaderAllocatorHeapNames_GetNames(MockTarget.Architecture arch)
    {
        (ISOSDacInterface13 impl, _) = CreateSOSDacImplForHeapTests(arch);

        int needed;
        int hr = impl.GetLoaderAllocatorHeapNames(0, null, &needed);
        Assert.Equal(MockHeapDictionary.Count, needed);

        char** names = stackalloc char*[needed];
        hr = impl.GetLoaderAllocatorHeapNames(needed, names, &needed);

        Assert.Equal(HResults.S_OK, hr);
        Assert.Equal(MockHeapDictionary.Count, needed);
        HashSet<string> expectedNames = new(MockHeapDictionary.Keys);
        for (int i = 0; i < needed; i++)
        {
            string actual = Marshal.PtrToStringAnsi((nint)names[i])!;
            Assert.Contains(actual, expectedNames);
        }
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetLoaderAllocatorHeapNames_InsufficientBuffer(MockTarget.Architecture arch)
    {
        (ISOSDacInterface13 impl, _) = CreateSOSDacImplForHeapTests(arch);

        int needed;
        char** names = stackalloc char*[2];
        int hr = impl.GetLoaderAllocatorHeapNames(2, names, &needed);

        Assert.Equal(HResults.S_FALSE, hr);
        Assert.Equal(MockHeapDictionary.Count, needed);
        HashSet<string> expectedNames = new(MockHeapDictionary.Keys);
        for (int i = 0; i < 2; i++)
        {
            string actual = Marshal.PtrToStringAnsi((nint)names[i])!;
            Assert.Contains(actual, expectedNames);
        }
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetLoaderAllocatorHeapNames_NullPNeeded(MockTarget.Architecture arch)
    {
        (ISOSDacInterface13 impl, _) = CreateSOSDacImplForHeapTests(arch);

        int hr = impl.GetLoaderAllocatorHeapNames(0, null, null);
        Assert.Equal(HResults.S_FALSE, hr);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetLoaderAllocatorHeaps_GetCount(MockTarget.Architecture arch)
    {
        (ISOSDacInterface13 impl, ClrDataAddress loaderAllocatorAddress) = CreateSOSDacImplForHeapTests(arch);

        int needed;
        int hr = impl.GetLoaderAllocatorHeaps(loaderAllocatorAddress, 0, null, null, &needed);

        Assert.Equal(HResults.S_OK, hr);
        Assert.Equal(MockHeapDictionary.Count, needed);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetLoaderAllocatorHeaps_GetHeaps(MockTarget.Architecture arch)
    {
        (ISOSDacInterface13 impl, ClrDataAddress loaderAllocatorAddress) = CreateSOSDacImplForHeapTests(arch);

        int needed;
        impl.GetLoaderAllocatorHeapNames(0, null, &needed);

        char** names = stackalloc char*[needed];
        impl.GetLoaderAllocatorHeapNames(needed, names, &needed);

        ClrDataAddress* heaps = stackalloc ClrDataAddress[needed];
        int* kinds = stackalloc int[needed];
        int hr = impl.GetLoaderAllocatorHeaps(loaderAllocatorAddress, needed, heaps, kinds, &needed);

        Assert.Equal(HResults.S_OK, hr);
        Assert.Equal(MockHeapDictionary.Count, needed);
        for (int i = 0; i < needed; i++)
        {
            string name = Marshal.PtrToStringAnsi((nint)names[i])!;
            Assert.Equal((ulong)MockHeapDictionary[name], (ulong)heaps[i]);
            Assert.Equal(0, kinds[i]); // LoaderHeapKindNormal
        }
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetLoaderAllocatorHeaps_InsufficientBuffer(MockTarget.Architecture arch)
    {
        (ISOSDacInterface13 impl, ClrDataAddress loaderAllocatorAddress) = CreateSOSDacImplForHeapTests(arch);

        ClrDataAddress* heaps = stackalloc ClrDataAddress[2];
        int* kinds = stackalloc int[2];
        int needed;
        int hr = impl.GetLoaderAllocatorHeaps(loaderAllocatorAddress, 2, heaps, kinds, &needed);

        Assert.Equal(HResults.E_INVALIDARG, hr);
        Assert.Equal(MockHeapDictionary.Count, needed);
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetLoaderAllocatorHeaps_NullAddress(MockTarget.Architecture arch)
    {
        (ISOSDacInterface13 impl, _) = CreateSOSDacImplForHeapTests(arch);

        int hr = impl.GetLoaderAllocatorHeaps(new ClrDataAddress(0), 0, null, null, null);

        Assert.Equal(HResults.E_INVALIDARG, hr);
    }

    private readonly record struct SectionDef(uint VirtualSize, uint VirtualAddress, uint SizeOfRawData, uint PointerToRawData);

    private static (Target Target, TargetPointer PEAssemblyAddr, TargetPointer ImageBase) CreateWebcilTarget(
        MockTarget.Architecture arch,
        ushort coffSections,
        SectionDef[] sections)
    {
        TargetTestHelpers helpers = new(arch);
        MockProcessBuilder processBuilder = new(arch);
        MockDataDescriptorType webcilHeaderType = null!;
        MockDataDescriptorType webcilSectionType = null!;
        ulong peAssemblyAddress = 0;

        processBuilder.AddCoreClr(module =>
        {
            module.AddDataDescriptor(descriptor =>
            {
                webcilHeaderType = descriptor.AddSequentialType("WebcilHeader", type =>
                {
                    type.AddField("CoffSections", sizeof(ushort));
                });

                webcilSectionType = descriptor.AddSequentialType("WebcilSectionHeader", type =>
                {
                    type.AddUInt32Field("VirtualSize");
                    type.AddUInt32Field("VirtualAddress");
                    type.AddUInt32Field("SizeOfRawData");
                    type.AddUInt32Field("PointerToRawData");
                });
            });
        });

        MockMemorySpace.BumpAllocator allocator = processBuilder.MemoryBuilder.DefaultAllocator;
        uint headerStride = webcilHeaderType.Size!.Value;
        uint sectionStride = webcilSectionType.Size!.Value;
        uint webcilImageSize = headerStride + sectionStride * (uint)sections.Length;
        MockMemorySpace.HeapFragment webcilImage = allocator.AllocateFragment(webcilImageSize, "WebcilImage");

        helpers.Write(
            webcilImage.Data.AsSpan().Slice(webcilHeaderType.GetFieldOffset(nameof(Data.WebcilHeader.CoffSections)), sizeof(ushort)),
            coffSections);

        for (int i = 0; i < sections.Length; i++)
        {
            int baseOffset = (int)headerStride + i * (int)sectionStride;
            helpers.Write(webcilImage.Data.AsSpan().Slice(baseOffset + webcilSectionType.GetFieldOffset(nameof(Data.WebcilSectionHeader.VirtualSize)), sizeof(uint)), sections[i].VirtualSize);
            helpers.Write(webcilImage.Data.AsSpan().Slice(baseOffset + webcilSectionType.GetFieldOffset(nameof(Data.WebcilSectionHeader.VirtualAddress)), sizeof(uint)), sections[i].VirtualAddress);
            helpers.Write(webcilImage.Data.AsSpan().Slice(baseOffset + webcilSectionType.GetFieldOffset(nameof(Data.WebcilSectionHeader.SizeOfRawData)), sizeof(uint)), sections[i].SizeOfRawData);
            helpers.Write(webcilImage.Data.AsSpan().Slice(baseOffset + webcilSectionType.GetFieldOffset(nameof(Data.WebcilSectionHeader.PointerToRawData)), sizeof(uint)), sections[i].PointerToRawData);
        }

        processBuilder.AddLoader(loader =>
        {
            ulong peImageLayoutAddress = loader.AddPEImageLayout(webcilImage.Address, webcilImageSize, format: 1);
            ulong peImageAddress = loader.AddPEImage(peImageLayoutAddress);
            peAssemblyAddress = loader.AddPEAssembly(peImageAddress);
        });

        MockProcess process = processBuilder.Build();
        Target target = process.CreateContractDescriptorTarget();
        return (target, new TargetPointer(peAssemblyAddress), new TargetPointer(webcilImage.Address));
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetILAddr_WebcilRvaToOffset(MockTarget.Architecture arch)
    {
        SectionDef[] sections =
        [
            new(VirtualSize: 0x2000, VirtualAddress: 0x1000, SizeOfRawData: 0x2000, PointerToRawData: 0x200),
            new(VirtualSize: 0x1000, VirtualAddress: 0x4000, SizeOfRawData: 0x1000, PointerToRawData: 0x2200),
        ];
        var (target, peAssemblyAddr, imageBase) = CreateWebcilTarget(arch, (ushort)sections.Length, sections);
        ILoader contract = target.Contracts.Loader;

        // RVA in first section: offset = (0x1100 - 0x1000) + 0x200 = 0x300
        Assert.Equal((TargetPointer)(imageBase + 0x300u), contract.GetILAddr(peAssemblyAddr, 0x1100));

        // RVA at start of first section: offset = (0x1000 - 0x1000) + 0x200 = 0x200
        Assert.Equal((TargetPointer)(imageBase + 0x200u), contract.GetILAddr(peAssemblyAddr, 0x1000));

        // RVA in second section: offset = (0x4500 - 0x4000) + 0x2200 = 0x2700
        Assert.Equal((TargetPointer)(imageBase + 0x2700u), contract.GetILAddr(peAssemblyAddr, 0x4500));
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetILAddr_WebcilNegativeRvaThrows(MockTarget.Architecture arch)
    {
        SectionDef[] sections =
        [
            new(VirtualSize: 0x2000, VirtualAddress: 0x1000, SizeOfRawData: 0x2000, PointerToRawData: 0x200),
        ];
        var (target, peAssemblyAddr, _) = CreateWebcilTarget(arch, 1, sections);
        ILoader contract = target.Contracts.Loader;

        Assert.Throws<InvalidOperationException>(() => contract.GetILAddr(peAssemblyAddr, -1));
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetILAddr_WebcilInvalidSectionCountThrows(MockTarget.Architecture arch)
    {
        var (targetZero, addrZero, _) = CreateWebcilTarget(arch, coffSections: 0, []);
        Assert.Throws<InvalidOperationException>(() => targetZero.Contracts.Loader.GetILAddr(addrZero, 0x1000));

        var (targetExcessive, addrExcessive, _) = CreateWebcilTarget(arch, coffSections: 17, []);
        Assert.Throws<InvalidOperationException>(() => targetExcessive.Contracts.Loader.GetILAddr(addrExcessive, 0x1000));
    }

    [Theory]
    [ClassData(typeof(MockTarget.StdArch))]
    public void GetILAddr_WebcilRvaNotInAnySectionThrows(MockTarget.Architecture arch)
    {
        SectionDef[] sections =
        [
            new(VirtualSize: 0x1000, VirtualAddress: 0x1000, SizeOfRawData: 0x1000, PointerToRawData: 0x200),
        ];
        var (target, peAssemblyAddr, _) = CreateWebcilTarget(arch, 1, sections);
        ILoader contract = target.Contracts.Loader;

        Assert.Throws<InvalidOperationException>(() => contract.GetILAddr(peAssemblyAddr, 0x5000));
    }
}

