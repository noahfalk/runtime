// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.DataContractReader;
using Microsoft.Diagnostics.DataContractReader.Contracts;

namespace Microsoft.Diagnostics.Internal.RuntimeMemoryMocks;

internal sealed class CdacAdapterState
{
    public Dictionary<DataType, Target.TypeInfo> Types { get; } = [];
    public List<(string Name, ulong Value)> Globals { get; } = [];
    public List<(string Name, string Value)> GlobalStrings { get; } = [];
    public List<(Type Type, Func<Target, IContract> Factory)> ContractFactories { get; } = [];
}
