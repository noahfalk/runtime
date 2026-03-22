// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DataContractReader.Contracts;
using Microsoft.Diagnostics.DataContractReader;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

internal static class MockProcessExtensions
{
    public static ContractDescriptorTarget CreateContractDescriptorTarget(this MockProcess process)
    {
        ulong descriptorAddress = FindRequiredExport(process, "DotNetRuntimeContractDescriptor");
        if (!ContractDescriptorTarget.TryCreate(
            descriptorAddress,
            process.ReadFromTarget,
            process.WriteToTarget,
            null,
            [],
            out ContractDescriptorTarget? target))
        {
            throw new InvalidOperationException($"Failed to create {nameof(ContractDescriptorTarget)} from mock process.");
        }

        return target;
    }

    private static ulong FindRequiredExport(MockProcess process, string exportName)
    {
        foreach (MockLoadedModule module in process.LoadedModules)
        {
            if (module.TryGetExport(exportName, out MockModuleExport? export))
            {
                return export.Address;
            }
        }

        throw new InvalidOperationException($"Export '{exportName}' is not present on this mock process.");
    }
}
