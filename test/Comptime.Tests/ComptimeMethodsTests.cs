using Xunit;

namespace Comptime.Tests;

public class ComptimeMethodsTests
{
    [Fact]
    public void GetPrimeNumbers_ReturnsPrimesUpTo30()
    {
        var primes = ComptimeMethods.GetPrimeNumbers();
        
        Assert.NotNull(primes);
        Assert.Equal(new[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29 }, primes);
    }

    [Fact]
    public void GetMagicNumber_Returns42()
    {
        var number = ComptimeMethods.GetMagicNumber();
        
        Assert.Equal(42, number);
    }

    [Fact]
    public void GetGreeting_ReturnsHelloWorld()
    {
        var greeting = ComptimeMethods.GetGreeting();
        
        Assert.Equal("Hello, World!", greeting);
    }

    [Fact]
    public void IsDebugMode_ReturnsFalse()
    {
        var isDebug = ComptimeMethods.IsDebugMode();
        
        Assert.False(isDebug);
    }

    [Fact]
    public void GetPi_ReturnsApproximatePi()
    {
        var pi = ComptimeMethods.GetPi();
        
        Assert.Equal(3.14159265358979, pi, 10);
    }

    [Fact]
    public void GetDaysOfWeek_ReturnsAllDays()
    {
        var days = ComptimeMethods.GetDaysOfWeek();
        
        Assert.NotNull(days);
        Assert.Equal(7, days.Count);
        Assert.Equal("Monday", days[0]);
        Assert.Equal("Sunday", days[6]);
    }

    [Fact]
    public void GetFibonacciNumbers_ReturnsFirst10FibonacciNumbers()
    {
        var fib = ComptimeMethods.GetFibonacciNumbers();
        
        Assert.NotNull(fib);
        Assert.Equal(10, fib.Count);
        Assert.Equal(new[] { 1, 1, 2, 3, 5, 8, 13, 21, 34, 55 }, fib);
    }

    [Fact]
    public void GetMonthNumbers_ReturnsAllMonths()
    {
        var months = ComptimeMethods.GetMonthNumbers();
        
        Assert.NotNull(months);
        Assert.Equal(12, months.Count);
        Assert.Equal(1, months["January"]);
        Assert.Equal(12, months["December"]);
    }

    [Fact]
    public void GetSpecialStrings_HandlesEscapeSequences()
    {
        var strings = ComptimeMethods.GetSpecialStrings();
        
        Assert.NotNull(strings);
        Assert.Equal(5, strings.Count);
        Assert.Equal("Hello\nWorld", strings[0]);
        Assert.Equal("Tab\tSeparated", strings[1]);
        Assert.Equal("Quote\"Inside", strings[2]);
        Assert.Equal("Backslash\\Path", strings[3]);
        Assert.Equal("Null\0Char", strings[4]);
    }

    [Fact]
    public void GetVowels_ReturnsAllVowels()
    {
        var vowels = ComptimeMethods.GetVowels();
        
        Assert.NotNull(vowels);
        Assert.Equal(new[] { 'a', 'e', 'i', 'o', 'u' }, vowels);
    }

    [Fact]
    public void Factorial_ReturnsCorrectValue()
    {
        Assert.Equal(1L, ComptimeMethods.Factorial(0));
        Assert.Equal(1L, ComptimeMethods.Factorial(1));
        Assert.Equal(2L, ComptimeMethods.Factorial(2));
        Assert.Equal(6L, ComptimeMethods.Factorial(3));
        Assert.Equal(120L, ComptimeMethods.Factorial(5));
        Assert.Equal(3628800L, ComptimeMethods.Factorial(10));
    }

    [Fact]
    public void Fibonacci_ReturnsCorrectValue()
    {
        Assert.Equal(0, ComptimeMethods.Fibonacci(0));
        Assert.Equal(1, ComptimeMethods.Fibonacci(1));
        Assert.Equal(1, ComptimeMethods.Fibonacci(2));
        Assert.Equal(2, ComptimeMethods.Fibonacci(3));
        Assert.Equal(5, ComptimeMethods.Fibonacci(5));
        Assert.Equal(55, ComptimeMethods.Fibonacci(10));
    }

    [Fact]
    public void Greet_ReturnsCorrectMessage()
    {
        Assert.Equal("Hello, World!", ComptimeMethods.Greet("World"));
        Assert.Equal("Hello, Alice!", ComptimeMethods.Greet("Alice"));
        Assert.Equal("Hello, Bob!", ComptimeMethods.Greet("Bob"));
    }

    [Fact]
    public void Add_ReturnsCorrectSum()
    {
        Assert.Equal(3, ComptimeMethods.Add(1, 2));
        Assert.Equal(0, ComptimeMethods.Add(0, 0));
        Assert.Equal(100, ComptimeMethods.Add(50, 50));
        Assert.Equal(-5, ComptimeMethods.Add(-10, 5));
    }

    [Fact]
    public void SumList_WithListInitializer_ReturnsSum()
    {
        // Test with list initializer syntax
        Assert.Equal(15, ComptimeMethods.SumList(new List<int> { 1, 2, 3, 4, 5 }));
        Assert.Equal(0, ComptimeMethods.SumList(new List<int>()));
        Assert.Equal(100, ComptimeMethods.SumList(new List<int> { 100 }));
    }

    [Fact]
    public void SumList_WithArrayInitializer_ReturnsSum()
    {
        // Test with array initializer syntax
        Assert.Equal(6, ComptimeMethods.SumList(new[] { 1, 2, 3 }));
        Assert.Equal(10, ComptimeMethods.SumList(new int[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void CountItems_ReturnsCorrectCount()
    {
        Assert.Equal(3, ComptimeMethods.CountItems(new List<int> { 1, 2, 3 }));
        Assert.Equal(5, ComptimeMethods.CountItems(new[] { "a", "b", "c", "d", "e" }));
        Assert.Equal(0, ComptimeMethods.CountItems(new List<string>()));
    }

    [Fact]
    public void JoinStrings_ReturnsJoinedString()
    {
        Assert.Equal("a,b,c", ComptimeMethods.JoinStrings(new[] { "a", "b", "c" }, ","));
        Assert.Equal("hello world", ComptimeMethods.JoinStrings(new List<string> { "hello", "world" }, " "));
        Assert.Equal("abc", ComptimeMethods.JoinStrings(new[] { "a", "b", "c" }, ""));
    }
}
