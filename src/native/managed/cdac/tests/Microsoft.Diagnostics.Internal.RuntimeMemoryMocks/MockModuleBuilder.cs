// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public sealed class MockModuleBuilder
{
    private const ulong DefaultModuleAlignment = 0x1000;

    private readonly Dictionary<string, MockModuleExport> _exports = new(StringComparer.Ordinal);
    private readonly List<Action<MockMemorySpace.BumpAllocator>> _onBuildCallbacks = [];

    internal MockModuleBuilder(string moduleName, MockTarget.Architecture architecture)
    {
        Name = moduleName;
        Path = $@"C:\mock\{moduleName}";
        Architecture = architecture;
    }

    public MockTarget.Architecture Architecture { get; }
    public string Name { get; }
    public string? Path { get; set; }
    public Dictionary<Type, object> Services { get; } = [];

    public MockModuleBuilder AddExport(string exportName, ulong address)
    {
        _exports[exportName] = new MockModuleExport(exportName, address);
        return this;
    }

    public MockModuleBuilder OnBuild(Action<MockMemorySpace.BumpAllocator> buildCallback)
    {
        _onBuildCallbacks.Add(buildCallback);
        return this;
    }

    internal MockLoadedModule Build(MockMemorySpace.BumpAllocator allocator)
    {
        allocator.AdvanceAligned(DefaultModuleAlignment);
        ulong baseAddress = allocator.Current;

        foreach (Action<MockMemorySpace.BumpAllocator> callback in _onBuildCallbacks)
        {
            callback(allocator);
        }
        if (allocator.Current == baseAddress)
        {
            allocator.AdvanceTo(checked(baseAddress + DefaultModuleAlignment));
        }

        ulong size = allocator.Current - baseAddress;

        List<MockModuleExport> exports = [.. _exports.Values];
        return new MockLoadedModule(baseAddress, size, Name, Path)
        {
            Exports = exports,
        };
    }
}
