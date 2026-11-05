using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Microsoft.Diagnostics.DataContractReader;

internal static class WasmDbgExactProfile
{
    internal enum Part
    {
        Definition, Arguments, ConstructedType, LoaderModule, TableSnapshot,
        CandidateHandle, CandidateMatch, TypeHandle, ReadMethodTable, ReadEEClass,
        ValidateRelations, ValidateFlags, LegacyCompare
    }

    internal enum Counter
    {
        ElementType, Assembly, MetadataToken, Arguments, ResultHandle,
        SnapshotEntries, Candidates, MatchPosition,
        HandleCacheHits, DataCacheHits, NewValidations
    }

    private sealed class State
    {
        internal readonly long Start = Stopwatch.GetTimestamp();
        internal readonly uint Thread = GetCurrentThreadId();
        internal readonly bool Legacy;
        internal readonly long[] Ticks = new long[Enum.GetValues<Part>().Length];
        internal readonly long[] Calls = new long[Enum.GetValues<Part>().Length];
        internal readonly int[] Depth = new int[Enum.GetValues<Part>().Length];
        internal readonly ulong[] Counters = new ulong[Enum.GetValues<Counter>().Length];
        internal State(bool legacy) => Legacy = legacy;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static readonly string? s_directory =
        Environment.GetEnvironmentVariable("DOTNET_WASMDBG_EXACT_PROFILE_DIR");
    [ThreadStatic] private static State? t_state;

    internal readonly struct Root : IDisposable
    {
        private readonly State? _previous;
        private readonly bool _enabled;
        internal Root(bool legacy)
        {
            _previous = t_state;
            _enabled = s_directory is not null;
            if (_enabled) t_state = new State(legacy);
        }
        public void Dispose()
        {
            if (!_enabled) return;
            State state = t_state!;
            long end = Stopwatch.GetTimestamp();
            t_state = _previous;
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("pid", Environment.ProcessId);
                writer.WriteNumber("thread", state.Thread);
                writer.WriteNumber("frequency", Stopwatch.Frequency);
                writer.WriteNumber("start", state.Start);
                writer.WriteNumber("end", end);
                writer.WriteBoolean("legacyComparison", state.Legacy);
                writer.WriteStartObject("parts");
                foreach (Part part in Enum.GetValues<Part>())
                {
                    writer.WriteStartObject(part.ToString());
                    writer.WriteNumber("calls", state.Calls[(int)part]);
                    writer.WriteNumber("ticks", state.Ticks[(int)part]);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
                writer.WriteStartObject("counters");
                foreach (Counter counter in Enum.GetValues<Counter>())
                    writer.WriteNumber(counter.ToString(), state.Counters[(int)counter]);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            File.AppendAllText(Path.Combine(s_directory!, $"cdac-exact-{Environment.ProcessId}.jsonl"),
                Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine);
        }
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly State? _state;
        private readonly Part _part;
        private readonly long _start;
        internal Scope(Part part)
        {
            _state = t_state;
            _part = part;
            _start = 0;
            if (_state is not null)
            {
                _state.Calls[(int)part]++;
                if (_state.Depth[(int)part]++ == 0) _start = Stopwatch.GetTimestamp();
            }
        }
        public void Dispose()
        {
            if (_state is not null && --_state.Depth[(int)_part] == 0)
                _state.Ticks[(int)_part] += Stopwatch.GetTimestamp() - _start;
        }
    }

    internal static Root Begin(bool legacy) => new(legacy);
    internal static Scope Measure(Part part) => new(part);
    internal static void Add(Counter counter, ulong value = 1)
    {
        if (t_state is not null) t_state.Counters[(int)counter] += value;
    }
}
