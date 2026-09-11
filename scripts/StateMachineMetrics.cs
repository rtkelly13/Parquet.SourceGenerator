#pragma warning disable CA1305, MA0009, MA0011, MA0023, MA0047, CA1852

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// -----------------------------------------------------------------------------
// StateMachineMetrics.cs
// Companion to InterrogateIL.cs (#257). Where InterrogateIL asks "does the emitted
// IL box?", this asks "how big is the emitted IL, and how many fields does the
// async state machine carry?".
//
// Reads metadata directly (System.Reflection.Metadata) rather than decompiling, so
// the numbers are the actual IL body sizes and the actual state-machine field
// counts, not an inference from source shape.
//
//   dotnet run scripts/StateMachineMetrics.cs -- --assembly <path> [--type <substring>]
// -----------------------------------------------------------------------------

string? assemblyPath = null;
string typeFilter = "ParquetExtensions";

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--assembly" when i + 1 < args.Length:
            assemblyPath = args[++i];
            break;
        case "--type" when i + 1 < args.Length:
            typeFilter = args[++i];
            break;
    }
}

if (assemblyPath is null || !File.Exists(assemblyPath))
{
    Console.Error.WriteLine("usage: --assembly <path.dll> [--type <substring>]");
    return 1;
}

using var fs = File.OpenRead(assemblyPath);
using var pe = new PEReader(fs);
MetadataReader md = pe.GetMetadataReader();

int MethodIlSize(MethodDefinition m)
{
    if (m.RelativeVirtualAddress == 0)
        return 0;
    return pe.GetMethodBody(m.RelativeVirtualAddress).GetILContent().Length;
}

string FullName(TypeDefinition t)
{
    string name = md.GetString(t.Name);
    string ns = md.GetString(t.Namespace);
    TypeDefinitionHandle decl = t.GetDeclaringType();
    if (!decl.IsNil)
        return FullName(md.GetTypeDefinition(decl)) + "+" + name;
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

var rows = new List<(string Owner, string Method, int Size, bool IsMoveNext)>();
var machines = new List<(string Type, int Fields, int MoveNextSize, List<string> FieldNames)>();
long totalIl = 0;

foreach (TypeDefinitionHandle th in md.TypeDefinitions)
{
    TypeDefinition t = md.GetTypeDefinition(th);
    string full = FullName(t);
    if (!full.Contains(typeFilter, StringComparison.Ordinal))
        continue;

    // An async state machine is a compiler-generated nested type implementing
    // IAsyncStateMachine. Detect it by the presence of a MoveNext method plus the
    // <>1__state field, which is stable across Roslyn versions.
    var fieldNames = new List<string>();
    foreach (FieldDefinitionHandle fh in t.GetFields())
        fieldNames.Add(md.GetString(md.GetFieldDefinition(fh).Name));

    int moveNextSize = 0;
    bool isMachine = fieldNames.Contains("<>1__state");

    foreach (MethodDefinitionHandle mh in t.GetMethods())
    {
        MethodDefinition m = md.GetMethodDefinition(mh);
        string mn = md.GetString(m.Name);
        int size = MethodIlSize(m);
        totalIl += size;
        bool isMoveNext = string.Equals(mn, "MoveNext", StringComparison.Ordinal);
        if (isMoveNext)
            moveNextSize = size;
        rows.Add((full, mn, size, isMoveNext));
    }

    if (isMachine)
        machines.Add((full, fieldNames.Count, moveNextSize, fieldNames));
}

Console.WriteLine($"assembly: {assemblyPath}");
Console.WriteLine($"type filter: *{typeFilter}*");
Console.WriteLine($"total IL bytes in matching types: {totalIl}");
Console.WriteLine();

Console.WriteLine("=== async state machines ===");
Console.WriteLine($"{"state machine", -78} {"fields", 6} {"MoveNext IL", 12}");
foreach (var m in machines.OrderByDescending(m => m.MoveNextSize))
{
    Console.WriteLine($"{Shorten(m.Type), -78} {m.Fields, 6} {m.MoveNextSize, 12}");
}
Console.WriteLine();
Console.WriteLine($"state machine count: {machines.Count}");
Console.WriteLine($"state machine field total: {machines.Sum(m => m.Fields)}");
Console.WriteLine($"MoveNext IL total: {machines.Sum(m => m.MoveNextSize)}");
Console.WriteLine();

Console.WriteLine("=== top 25 methods by IL size ===");
foreach (var r in rows.OrderByDescending(r => r.Size).Take(25))
{
    Console.WriteLine($"{r.Size, 8}  {Shorten(r.Owner)}.{r.Method}");
}
Console.WriteLine();

Console.WriteLine("=== state machine fields (detail) ===");
foreach (var m in machines.OrderByDescending(m => m.Fields))
{
    Console.WriteLine($"{Shorten(m.Type)} ({m.Fields} fields)");
    foreach (string f in m.FieldNames)
        Console.WriteLine($"    {f}");
}

return 0;

static string Shorten(string s) => s.Replace("SampleDomain.Models.", "", StringComparison.Ordinal);
