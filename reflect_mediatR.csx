#r "tests/NetMediate.Benchmarks/bin/Release/net10.0/MediatR.dll"
#r "tests/NetMediate.Benchmarks/bin/Release/net10.0/MediatR.Contracts.dll"

using System;
using System.Reflection;
using System.Linq;

var assembly = typeof(MediatR.Mediator).Assembly;

// List all fields on MediatR.Mediator
Console.WriteLine("=== MediatR.Mediator Fields ===");
foreach (var f in typeof(MediatR.Mediator).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
    Console.WriteLine($"  [{(f.IsStatic ? "static" : "instance")}] {f.FieldType.Name} {f.Name}");

// Check ServiceCollectionExtensions
Console.WriteLine("\n=== ServiceCollectionExtensions ===");
var ext = assembly.GetType("MediatR.Registration.ServiceRegistrar");
if (ext != null)
{
    foreach (var m in ext.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
        Console.WriteLine($"  {m.Name}");
}

// Check all types in MediatR.Registration namespace
Console.WriteLine("\n=== MediatR Registration types ===");
foreach (var t in assembly.GetTypes().Where(t => t.Namespace?.Contains("Registration") == true || t.Name.Contains("ServiceCollectionExtensions") || t.Name.Contains("Registration")))
    Console.WriteLine($"  {t.FullName}");
