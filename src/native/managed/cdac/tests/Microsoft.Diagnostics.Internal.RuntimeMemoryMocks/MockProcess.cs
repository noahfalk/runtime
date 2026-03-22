// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

public sealed record MockThread(ulong Id, ulong OsId, string? Name = null);

public sealed record MockModuleExport(string Name, ulong Address);

public sealed record MockLoadedModule(ulong BaseAddress, ulong Size, string Name, string? Path = null)
{
    public IReadOnlyList<MockModuleExport> Exports { get; init; } = [];

    public bool TryGetExport(string name, [NotNullWhen(true)] out MockModuleExport? export)
    {
        foreach (MockModuleExport candidate in Exports)
        {
            if (candidate.Name == name)
            {
                export = candidate;
                return true;
            }
        }

        export = null;
        return false;
    }
}

public sealed class MockProcess
{
    internal MockProcess(
        MockTarget.Architecture architecture,
        MockMemorySpace.MemoryContext virtualMemory,
        IReadOnlyList<MockThread> threads,
        IReadOnlyList<MockLoadedModule> loadedModules)
    {
        Architecture = architecture;
        VirtualMemory = virtualMemory;
        Threads = threads;
        LoadedModules = loadedModules;
    }

    public MockTarget.Architecture Architecture { get; }
    public MockMemorySpace.MemoryContext VirtualMemory { get; }
    public IReadOnlyList<MockThread> Threads { get; }
    public IReadOnlyList<MockLoadedModule> LoadedModules { get; }

    public int ReadFromTarget(ulong address, Span<byte> buffer) => VirtualMemory.ReadFromTarget(address, buffer);
    public int WriteToTarget(ulong address, Span<byte> buffer) => VirtualMemory.WriteToTarget(address, buffer);
}
