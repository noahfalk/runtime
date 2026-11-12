// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;

namespace Microsoft.Diagnostics.DataContractReader.Legacy;

[GeneratedComInterface]
[Guid("AF1D836E-AB24-4472-8E87-2A13B94D391D")]
public unsafe partial interface IDacDbiFuncEvalBuffer
{
    [PreserveSig]
    int GetBufferStateAddress(ulong* address);

    [PreserveSig]
    int ValidateFuncEval(ulong thread, int duringException);
}

public sealed unsafe partial class DacDbiImpl : IDacDbiFuncEvalBuffer
{
    public int GetBufferStateAddress(ulong* address)
    {
        using Lock.Scope scope = _apiLock.EnterScope();
        if (address == null)
            return HResults.E_INVALIDARG;
        *address = 0;
        try
        {
            if (!_target.TryReadGlobalPointer("FuncEvalBuffer", out TargetPointer? state))
                return HResults.E_NOTIMPL;
            *address = state.Value.Value;
            return HResults.S_OK;
        }
        catch (Exception ex)
        {
            return ex.HResult;
        }
    }

    public int ValidateFuncEval(ulong thread, int duringException)
    {
        using Lock.Scope scope = _apiLock.EnterScope();
        try
        {
            Data.Thread data = _target.ProcessedData.GetOrAdd<Data.Thread>(thread);
            if ((data.State & 1) != 0)
                return unchecked((int)0x80131313); // CORDBG_E_FUNC_EVAL_BAD_START_POINT
            if (duringException == 0)
            {
                ulong context;
                int hr = GetManagedStoppedContext(thread, &context);
                if (hr != HResults.S_OK)
                    return hr;
                if (context == 0)
                    return unchecked((int)0x80131c23); // CORDBG_E_ILLEGAL_AT_GC_UNSAFE_POINT
            }
            return HResults.S_OK;
        }
        catch (Exception ex)
        {
            return ex.HResult;
        }
    }
}
