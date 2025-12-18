using Xunit;
using Comptime;

namespace Comptime.Tests;

/// <summary>
/// Tests for the CSharpSerializer class directly.
/// </summary>
public class CSharpSerializerTests
{
    #region Primitive Types

    [Fact]
    public void Serialize_Boolean_True()
    {
        var result = CSharpSerializer.Serialize(true, typeof(bool));
        Assert.Equal("true", result);
    }

    [Fact]
    public void Serialize_Boolean_False()
    {
        var result = CSharpSerializer.Serialize(false, typeof(bool));
        Assert.Equal("false", result);
    }

    [Fact]
    public void Serialize_Int32_Positive()
    {
        var result = CSharpSerializer.Serialize(42, typeof(int));
        Assert.Equal("42", result);
    }

    [Fact]
    public void Serialize_Int32_Negative()
    {
        var result = CSharpSerializer.Serialize(-123, typeof(int));
        Assert.Equal("-123", result);
    }

    [Fact]
    public void Serialize_Int64()
    {
        var result = CSharpSerializer.Serialize(9876543210L, typeof(long));
        Assert.Equal("9876543210L", result);
    }

    [Fact]
    public void Serialize_UInt32()
    {
        var result = CSharpSerializer.Serialize(42u, typeof(uint));
        Assert.Equal("42u", result);
    }

    [Fact]
    public void Serialize_UInt64()
    {
        var result = CSharpSerializer.Serialize(42UL, typeof(ulong));
        Assert.Equal("42UL", result);
    }

    [Fact]
    public void Serialize_Byte()
    {
        var result = CSharpSerializer.Serialize((byte)255, typeof(byte));
        Assert.Equal("(byte)255", result);
    }

    [Fact]
    public void Serialize_SByte()
    {
        var result = CSharpSerializer.Serialize((sbyte)-128, typeof(sbyte));
        Assert.Equal("(sbyte)-128", result);
    }

    [Fact]
    public void Serialize_Int16()
    {
        var result = CSharpSerializer.Serialize((short)1234, typeof(short));
        Assert.Equal("(short)1234", result);
    }

    [Fact]
    public void Serialize_UInt16()
    {
        var result = CSharpSerializer.Serialize((ushort)1234, typeof(ushort));
        Assert.Equal("(ushort)1234", result);
    }

    [Fact]
    public void Serialize_Float()
    {
        var result = CSharpSerializer.Serialize(3.14f, typeof(float));
        Assert.Contains("3.14", result);
        Assert.EndsWith("f", result);
    }

    [Fact]
    public void Serialize_Float_NaN()
    {
        var result = CSharpSerializer.Serialize(float.NaN, typeof(float));
        Assert.Equal("float.NaN", result);
    }

    [Fact]
    public void Serialize_Float_PositiveInfinity()
    {
        var result = CSharpSerializer.Serialize(float.PositiveInfinity, typeof(float));
        Assert.Equal("float.PositiveInfinity", result);
    }

    [Fact]
    public void Serialize_Double()
    {
        var result = CSharpSerializer.Serialize(3.14159265358979d, typeof(double));
        Assert.Contains("3.14159265358979", result);
        Assert.EndsWith("d", result);
    }

    [Fact]
    public void Serialize_Double_NaN()
    {
        var result = CSharpSerializer.Serialize(double.NaN, typeof(double));
        Assert.Equal("double.NaN", result);
    }

    [Fact]
    public void Serialize_Decimal()
    {
        var result = CSharpSerializer.Serialize(123.45m, typeof(decimal));
        Assert.Equal("123.45m", result);
    }

    #endregion

    #region Characters and Strings

    [Fact]
    public void Serialize_Char_Simple()
    {
        var result = CSharpSerializer.Serialize('A', typeof(char));
        Assert.Equal("'A'", result);
    }

    [Fact]
    public void Serialize_Char_Newline()
    {
        var result = CSharpSerializer.Serialize('\n', typeof(char));
        Assert.Equal("'\\n'", result);
    }

    [Fact]
    public void Serialize_Char_Tab()
    {
        var result = CSharpSerializer.Serialize('\t', typeof(char));
        Assert.Equal("'\\t'", result);
    }

    [Fact]
    public void Serialize_Char_SingleQuote()
    {
        var result = CSharpSerializer.Serialize('\'', typeof(char));
        Assert.Equal("'\\''", result);
    }

    [Fact]
    public void Serialize_Char_Backslash()
    {
        var result = CSharpSerializer.Serialize('\\', typeof(char));
        Assert.Equal("'\\\\'", result);
    }

    [Fact]
    public void Serialize_String_Simple()
    {
        var result = CSharpSerializer.Serialize("Hello", typeof(string));
        Assert.Equal("\"Hello\"", result);
    }

    [Fact]
    public void Serialize_String_WithQuotes()
    {
        var result = CSharpSerializer.Serialize("Say \"Hello\"", typeof(string));
        Assert.Equal("\"Say \\\"Hello\\\"\"", result);
    }

    [Fact]
    public void Serialize_String_WithNewlines()
    {
        var result = CSharpSerializer.Serialize("Line1\nLine2", typeof(string));
        Assert.Equal("\"Line1\\nLine2\"", result);
    }

    [Fact]
    public void Serialize_String_WithTabs()
    {
        var result = CSharpSerializer.Serialize("Col1\tCol2", typeof(string));
        Assert.Equal("\"Col1\\tCol2\"", result);
    }

    [Fact]
    public void Serialize_String_WithBackslashes()
    {
        var result = CSharpSerializer.Serialize("C:\\Path\\File", typeof(string));
        Assert.Equal("\"C:\\\\Path\\\\File\"", result);
    }

    [Fact]
    public void Serialize_Null()
    {
        var result = CSharpSerializer.Serialize(null, typeof(string));
        Assert.Equal("null", result);
    }

    #endregion

    #region Arrays

    [Fact]
    public void Serialize_IntArray()
    {
        var result = CSharpSerializer.Serialize(new[] { 1, 2, 3 }, typeof(int[]));
        Assert.Equal("new int[] { 1, 2, 3 }", result);
    }

    [Fact]
    public void Serialize_IntArray_Empty()
    {
        var result = CSharpSerializer.Serialize(Array.Empty<int>(), typeof(int[]));
        Assert.Equal("new int[] {  }", result);
    }

    [Fact]
    public void Serialize_StringArray()
    {
        var result = CSharpSerializer.Serialize(new[] { "a", "b", "c" }, typeof(string[]));
        Assert.Equal("new string[] { \"a\", \"b\", \"c\" }", result);
    }

    [Fact]
    public void Serialize_CharArray()
    {
        var result = CSharpSerializer.Serialize(new[] { 'a', 'b', 'c' }, typeof(char[]));
        Assert.Equal("new char[] { 'a', 'b', 'c' }", result);
    }

    #endregion

    #region Collections

    [Fact]
    public void Serialize_ListOfInt()
    {
        var list = new List<int> { 1, 2, 3 };
        var result = CSharpSerializer.Serialize(list, typeof(IReadOnlyList<int>));
        Assert.Equal("new int[] { 1, 2, 3 }", result);
    }

    [Fact]
    public void Serialize_Dictionary()
    {
        var dict = new Dictionary<string, int>
        {
            { "one", 1 },
            { "two", 2 }
        };
        var result = CSharpSerializer.Serialize(dict, typeof(IReadOnlyDictionary<string, int>));
        Assert.Contains("new global::System.Collections.Generic.Dictionary<string, int>", result);
        Assert.Contains("{ \"one\", 1 }", result);
        Assert.Contains("{ \"two\", 2 }", result);
    }

    #endregion

    #region CanSerialize

    [Theory]
    [InlineData(typeof(bool), true)]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(int[]), false)] // arrays are not allowed (not immutable)
    [InlineData(typeof(string[]), false)] // arrays are not allowed (not immutable)
    [InlineData(typeof(List<int>), true)]
    [InlineData(typeof(IReadOnlyList<string>), true)]
    [InlineData(typeof(Dictionary<string, int>), true)]
    [InlineData(typeof(object), false)] // object is not serializable
    [InlineData(typeof(DateTime), false)] // DateTime is not supported
    public void CanSerialize_ReturnsExpectedResult(Type type, bool expected)
    {
        var result = CSharpSerializer.CanSerialize(type);
        Assert.Equal(expected, result);
    }

    #endregion

    #region GetTypeName

    [Theory]
    [InlineData(typeof(bool), "bool")]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(float), "float")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(decimal), "decimal")]
    [InlineData(typeof(char), "char")]
    [InlineData(typeof(int[]), "int[]")]
    [InlineData(typeof(string[]), "string[]")]
    public void GetTypeName_ReturnsExpectedResult(Type type, string expected)
    {
        var result = CSharpSerializer.GetTypeName(type);
        Assert.Equal(expected, result);
    }

    #endregion
}
