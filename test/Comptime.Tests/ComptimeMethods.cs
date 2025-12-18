namespace Comptime.Tests;

/// <summary>
/// Contains methods marked with [Comptime] that will be executed at compile time
/// and have their results serialized to C# code.
/// </summary>
public static partial class ComptimeMethods
{
    /// <summary>
    /// Returns a list of prime numbers computed at compile time.
    /// </summary>
    [Comptime]
    public static IReadOnlyList<int> GetPrimeNumbers()
    {
        var primes = new List<int>();
        for (int i = 2; i <= 30; i++)
        {
            if (IsPrime(i))
            {
                primes.Add(i);
            }
        }
        return primes;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n == 2) return true;
        if (n % 2 == 0) return false;
        for (int i = 3; i * i <= n; i += 2)
        {
            if (n % i == 0) return false;
        }
        return true;
    }

    /// <summary>
    /// Returns a simple integer computed at compile time.
    /// </summary>
    [Comptime]
    public static int GetMagicNumber()
    {
        return 42;
    }

    /// <summary>
    /// Returns a string computed at compile time.
    /// </summary>
    [Comptime]
    public static string GetGreeting()
    {
        return "Hello, World!";
    }

    /// <summary>
    /// Returns a boolean computed at compile time.
    /// </summary>
    [Comptime]
    public static bool IsDebugMode()
    {
        return false;
    }

    /// <summary>
    /// Returns a double computed at compile time.
    /// </summary>
    [Comptime]
    public static double GetPi()
    {
        return 3.14159265358979;
    }

    /// <summary>
    /// Returns a list of strings computed at compile time.
    /// </summary>
    [Comptime]
    public static IReadOnlyList<string> GetDaysOfWeek()
    {
        return new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
    }

    /// <summary>
    /// Returns a list (serialized as array) computed at compile time.
    /// </summary>
    [Comptime]
    public static IReadOnlyList<int> GetFibonacciNumbers()
    {
        var fib = new List<int> { 1, 1 };
        for (int i = 2; i < 10; i++)
        {
            fib.Add(fib[i - 1] + fib[i - 2]);
        }
        return fib;
    }

    /// <summary>
    /// Returns a dictionary computed at compile time.
    /// </summary>
    [Comptime]
    public static IReadOnlyDictionary<string, int> GetMonthNumbers()
    {
        return new Dictionary<string, int>
        {
            { "January", 1 },
            { "February", 2 },
            { "March", 3 },
            { "April", 4 },
            { "May", 5 },
            { "June", 6 },
            { "July", 7 },
            { "August", 8 },
            { "September", 9 },
            { "October", 10 },
            { "November", 11 },
            { "December", 12 }
        };
    }

    /// <summary>
    /// Returns a list with special characters in strings.
    /// </summary>
    [Comptime]
    public static IReadOnlyList<string> GetSpecialStrings()
    {
        return new[]
        {
            "Hello\nWorld",
            "Tab\tSeparated",
            "Quote\"Inside",
            "Backslash\\Path",
            "Null\0Char"
        };
    }

    /// <summary>
    /// Returns a list of chars.
    /// </summary>
    [Comptime]
    public static IReadOnlyList<char> GetVowels()
    {
        return new[] { 'a', 'e', 'i', 'o', 'u' };
    }

    /// <summary>
    /// Returns the factorial of n computed at compile time.
    /// </summary>
    [Comptime]
    public static long Factorial(int n)
    {
        if (n <= 1) return 1;
        long result = 1;
        for (int i = 2; i <= n; i++)
        {
            result *= i;
        }
        return result;
    }

    /// <summary>
    /// Returns the nth Fibonacci number computed at compile time.
    /// </summary>
    [Comptime]
    public static int Fibonacci(int n)
    {
        if (n <= 0) return 0;
        if (n == 1) return 1;
        
        int a = 0, b = 1;
        for (int i = 2; i <= n; i++)
        {
            int temp = a + b;
            a = b;
            b = temp;
        }
        return b;
    }

    /// <summary>
    /// Returns a greeting message with the given name computed at compile time.
    /// </summary>
    [Comptime]
    public static string Greet(string name)
    {
        return $"Hello, {name}!";
    }

    /// <summary>
    /// Returns the sum of two numbers computed at compile time.
    /// </summary>
    [Comptime]
    public static int Add(int a, int b)
    {
        return a + b;
    }

    /// <summary>
    /// Returns the sum of all numbers in a list computed at compile time.
    /// </summary>
    [Comptime]
    public static int SumList(IReadOnlyList<int> numbers)
    {
        return numbers.Sum();
    }

    /// <summary>
    /// Returns the count of items in a collection computed at compile time.
    /// </summary>
    [Comptime]
    public static int CountItems<T>(IReadOnlyList<T> items)
    {
        return items.Count;
    }

    /// <summary>
    /// Returns the concatenation of strings computed at compile time.
    /// </summary>
    [Comptime]
    public static string JoinStrings(IReadOnlyList<string> strings, string separator)
    {
        return string.Join(separator, strings);
    }
}
