// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public sealed class MockProcessBuilder
{
    private const string CoreClrModuleName = "coreclr.dll";
    private const ulong ModuleRegionStart = 0x5000_0000;
    private const ulong ModuleRegionSize = 0x1000_0000;
    private readonly Dictionary<string, MockModuleBuilder> _modulesByName = new(StringComparer.Ordinal);
    private readonly MockMemoryHelpers _memoryHelpers;
    private readonly List<MockThread> _threads = [];

    public MockProcessBuilder(MockTarget.Architecture architecture)
        : this(architecture, new MockMemoryHelpers(architecture))
    {
    }

    public MockProcessBuilder(MockTarget.Architecture architecture, MockMemoryHelpers memoryHelpers)
    {
        Architecture = architecture;
        _memoryHelpers = memoryHelpers;
        MemoryBuilder = new MockMemorySpace.Builder(_memoryHelpers);
    }

    public MockTarget.Architecture Architecture { get; }
    public MockMemorySpace.Builder MemoryBuilder { get; }

    public MockProcessBuilder AddThread(MockThread thread)
    {
        _threads.Add(thread);
        return this;
    }

    public MockProcessBuilder AddModule(string moduleName, Action<MockModuleBuilder> configure)
    {
        if (!_modulesByName.TryGetValue(moduleName, out MockModuleBuilder? moduleBuilder))
        {
            moduleBuilder = new MockModuleBuilder(moduleName, Architecture);
            _modulesByName.Add(moduleName, moduleBuilder);
        }

        configure(moduleBuilder);
        return this;
    }

    public MockProcessBuilder AddCoreClr(Action<MockModuleBuilder> configure)
        => AddModule(CoreClrModuleName, configure);

    public MockProcess Build()
    {
        List<MockLoadedModule> modules = BuildModules();
        return new MockProcess(
            Architecture,
            MemoryBuilder.GetMemoryContext(),
            [.. _threads],
            [.. modules]);
    }

    private List<MockLoadedModule> BuildModules()
    {
        List<MockLoadedModule> _loadedModules = [];
        MockMemorySpace.BumpAllocator moduleAllocator = new(ModuleRegionStart, checked(ModuleRegionStart + ModuleRegionSize), Architecture);
        foreach (MockModuleBuilder moduleBuilder in _modulesByName.Values)
        {
            _loadedModules.Add(moduleBuilder.Build(moduleAllocator));
        }
        foreach (MockMemorySpace.HeapFragment fragment in moduleAllocator.Allocations)
        {
            if (fragment.Data is not null && fragment.Data.Length > 0)
            {
                MemoryBuilder.AddHeapFragment(fragment);
            }
        }
        return _loadedModules;
    }
}
