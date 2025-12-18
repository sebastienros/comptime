# Comptime

A .NET source generator that executes methods at compile time and serializes their results to C# code. Comptime brings meta-programming capabilities to C#, enabling compile-time code generation and evaluation.

## Overview

Comptime allows you to mark methods with the `[Comptime]` attribute to have them executed during compilation. The return values are serialized into C# source code and used at runtime, eliminating the need for runtime computation of values that can be determined at build time.

This meta-programming approach enables developers to shift expensive computations from runtime to compile time, resulting in faster application startup and execution.

## Features

- **Compile-time execution**: Methods marked with `[Comptime]` are executed during compilation
- **Method parameters**: Methods can accept parameters with compile-time constant expressions
- **C# serialization**: Results are serialized to valid C# code
- **Supported return types**: 
  - Primitive types: `int`, `long`, `short`, `byte`, `sbyte`, `uint`, `ulong`, `ushort`, `float`, `double`, `decimal`, `bool`, `char`, `string`
  - Collections: `IReadOnlyList<T>`, `IReadOnlyDictionary<TKey, TValue>`, `List<T>`, `Dictionary<TKey, TValue>`
  - Note: Arrays are **not** allowed as return types because they are mutable. Use `IReadOnlyList<T>` instead.
- **Supported argument types**: Any expression that doesn't contain variables, including:
  - Literals: `42`, `"hello"`, `true`
  - Collection initializers: `new List<int> { 1, 2, 3 }`, `new[] { "a", "b", "c" }`
  - Expressions: `1 + 2`, `Math.PI * 2`
  - Const values and enum members
- **Interceptor-based**: Uses C# interceptors to replace method calls with pre-computed values

## Usage

### Basic Usage (Parameterless Methods)

```csharp
using Comptime;

public static partial class Constants
{
    [Comptime]
    public static IReadOnlyList<int> GetPrimeNumbers()
    {
        // Complex computation that runs at compile time
        var primes = new List<int>();
        for (int i = 2; i <= 100; i++)
        {
            if (IsPrime(i))
                primes.Add(i);
        }
        return primes;
    }
    
    private static bool IsPrime(int n) { /* ... */ }
}

// At runtime, calling GetPrimeNumbers() returns the pre-computed list
var primes = Constants.GetPrimeNumbers(); // Returns [2, 3, 5, 7, 11, ...]
```

### Methods with Parameters

```csharp
using Comptime;

public static partial class Math
{
    [Comptime]
    public static long Factorial(int n)
    {
        if (n <= 1) return 1;
        long result = 1;
        for (int i = 2; i <= n; i++)
            result *= i;
        return result;
    }

    [Comptime]
    public static int SumList(IReadOnlyList<int> numbers)
    {
        return numbers.Sum();
    }
}

// Each unique argument combination is computed at compile time
var fact5 = Math.Factorial(5);   // Pre-computed: 120
var fact10 = Math.Factorial(10); // Pre-computed: 3628800

// Collection initializers work too!
var sum = Math.SumList(new List<int> { 1, 2, 3, 4, 5 }); // Pre-computed: 15
var sum2 = Math.SumList(new[] { 10, 20, 30 });           // Pre-computed: 60
```

### Generic Methods

```csharp
using Comptime;

public static partial class Utils
{
    [Comptime]
    public static int CountItems<T>(IReadOnlyList<T> items)
    {
        return items.Count;
    }

    [Comptime]
    public static string JoinStrings(IReadOnlyList<string> strings, string separator)
    {
        return string.Join(separator, strings);
    }
}

var count = Utils.CountItems(new[] { "a", "b", "c" }); // Pre-computed: 3
var joined = Utils.JoinStrings(new[] { "hello", "world" }, " "); // Pre-computed: "hello world"
```

## Requirements

- .NET 8.0 or later
- C# 12 or later (for interceptors support)

## Installation

```xml
<PackageReference Include="Comptime" Version="1.0.0" />
```

## How It Works

1. The source generator finds methods marked with `[Comptime]`
2. It identifies all call sites and their arguments
3. For each unique argument combination, it executes the method at compile time
4. The return values are serialized to C# literals/expressions
5. Interceptor methods are generated that return the pre-computed values
6. At runtime, calls to the original methods are intercepted and return the cached values

## Diagnostics

| Code | Description |
|------|-------------|
| COMPTIME001 | Class must be partial |
| COMPTIME002 | Method must be static |
| COMPTIME004 | Unsupported return type |
| COMPTIME005 | Compilation emit failed |
| COMPTIME006 | Method execution failed |
| COMPTIME007 | Serialization failed |
| COMPTIME011 | Array return type not allowed (use IReadOnlyList<T>) |
| COMPTIME012 | Argument must be a constant (no variables allowed) |

## Limitations

- Methods must be `static`
- The containing class must be `partial`
- Return types must be immutable (arrays are not allowed, use `IReadOnlyList<T>`)
- Method arguments must be compile-time constant expressions (no variables, only literals and expressions of literals)
- Methods cannot have side effects that depend on runtime state

## License

MIT License
