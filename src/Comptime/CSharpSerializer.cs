using System.Collections;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Comptime;

/// <summary>
/// Provides serialization of .NET objects to C# source code literals.
/// This class can serialize primitive types, strings, and common collection types
/// into valid C# expressions that recreate those values at runtime.
/// </summary>
public static class CSharpSerializer
{
    /// <summary>
    /// Attempts to serialize an object to a C# expression.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="targetType">The target type for the serialization.</param>
    /// <param name="result">The resulting C# expression if successful.</param>
    /// <param name="error">An error message if serialization fails.</param>
    /// <returns>True if serialization succeeded, false otherwise.</returns>
    public static bool TrySerialize(object? value, Type targetType, out string result, out string? error)
    {
        result = string.Empty;
        error = null;

        try
        {
            result = Serialize(value, targetType);
            return true;
        }
        catch (NotSupportedException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Serializes an object to a C# expression.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="targetType">The target type for the serialization.</param>
    /// <returns>A C# expression that recreates the value.</returns>
    /// <exception cref="NotSupportedException">Thrown when the type cannot be serialized.</exception>
    public static string Serialize(object? value, Type targetType)
    {
        if (value is null)
        {
            return "null";
        }

        var actualType = value.GetType();

        // Handle primitive types
        if (TrySerializePrimitive(value, actualType, out var primitiveResult))
        {
            return primitiveResult;
        }

        // Handle arrays
        if (actualType.IsArray)
        {
            return SerializeArray((Array)value, actualType.GetElementType()!);
        }

        // Handle generic types (collections, dictionaries)
        if (actualType.IsGenericType)
        {
            return SerializeGenericType(value, actualType, targetType);
        }

        throw new NotSupportedException($"Type '{actualType.FullName}' is not supported for C# serialization.");
    }

    private static bool TrySerializePrimitive(object value, Type type, out string result)
    {
        result = string.Empty;

        if (type == typeof(bool))
        {
            result = (bool)value ? "true" : "false";
            return true;
        }

        if (type == typeof(byte))
        {
            result = $"(byte){((byte)value).ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        if (type == typeof(sbyte))
        {
            result = $"(sbyte){((sbyte)value).ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        if (type == typeof(short))
        {
            result = $"(short){((short)value).ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        if (type == typeof(ushort))
        {
            result = $"(ushort){((ushort)value).ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        if (type == typeof(int))
        {
            result = ((int)value).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (type == typeof(uint))
        {
            result = ((uint)value).ToString(CultureInfo.InvariantCulture) + "u";
            return true;
        }

        if (type == typeof(long))
        {
            result = ((long)value).ToString(CultureInfo.InvariantCulture) + "L";
            return true;
        }

        if (type == typeof(ulong))
        {
            result = ((ulong)value).ToString(CultureInfo.InvariantCulture) + "UL";
            return true;
        }

        if (type == typeof(float))
        {
            var f = (float)value;
            if (float.IsNaN(f)) { result = "float.NaN"; return true; }
            if (float.IsPositiveInfinity(f)) { result = "float.PositiveInfinity"; return true; }
            if (float.IsNegativeInfinity(f)) { result = "float.NegativeInfinity"; return true; }
            result = f.ToString("G9", CultureInfo.InvariantCulture) + "f";
            return true;
        }

        if (type == typeof(double))
        {
            var d = (double)value;
            if (double.IsNaN(d)) { result = "double.NaN"; return true; }
            if (double.IsPositiveInfinity(d)) { result = "double.PositiveInfinity"; return true; }
            if (double.IsNegativeInfinity(d)) { result = "double.NegativeInfinity"; return true; }
            result = d.ToString("G17", CultureInfo.InvariantCulture) + "d";
            return true;
        }

        if (type == typeof(decimal))
        {
            result = ((decimal)value).ToString(CultureInfo.InvariantCulture) + "m";
            return true;
        }

        if (type == typeof(char))
        {
            result = SerializeChar((char)value);
            return true;
        }

        if (type == typeof(string))
        {
            result = SerializeString((string)value);
            return true;
        }

        return false;
    }

    private static string SerializeChar(char c)
    {
        return SyntaxFactory.Literal(c).ToString();
    }

    private static string SerializeString(string s)
    {
        return SyntaxFactory.Literal(s).ToString();
    }

    private static string SerializeArray(Array array, Type elementType)
    {
        var sb = new StringBuilder();
        sb.Append("new ");
        sb.Append(GetTypeName(elementType));
        sb.Append("[] { ");

        var first = true;
        foreach (var item in array)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(Serialize(item, elementType));
        }

        sb.Append(" }");
        return sb.ToString();
    }

    private static string SerializeGenericType(object value, Type actualType, Type targetType)
    {
        var genericDef = actualType.GetGenericTypeDefinition();
        var genericArgs = actualType.GetGenericArguments();

        // Handle List<T>, IList<T>, IReadOnlyList<T> - serialize as array
        if (genericDef == typeof(List<>) ||
            IsAssignableToGenericType(actualType, typeof(IList<>)) ||
            IsAssignableToGenericType(actualType, typeof(IReadOnlyList<>)))
        {
            var elementType = genericArgs[0];
            return SerializeListAsArray((IEnumerable)value, elementType, targetType);
        }

        // Handle Dictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
        if (genericDef == typeof(Dictionary<,>) ||
            IsAssignableToGenericType(actualType, typeof(IReadOnlyDictionary<,>)))
        {
            var keyType = genericArgs[0];
            var valueType = genericArgs[1];
            return SerializeDictionary((IDictionary)value, keyType, valueType, targetType);
        }

        throw new NotSupportedException($"Generic type '{actualType.FullName}' is not supported for C# serialization.");
    }

    private static string SerializeListAsArray(IEnumerable enumerable, Type elementType, Type targetType)
    {
        var sb = new StringBuilder();
        sb.Append("new ");
        sb.Append(GetTypeName(elementType));
        sb.Append("[] { ");

        var first = true;
        foreach (var item in enumerable)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(Serialize(item, elementType));
        }

        sb.Append(" }");
        return sb.ToString();
    }

    private static string SerializeDictionary(IDictionary dictionary, Type keyType, Type valueType, Type targetType)
    {
        var sb = new StringBuilder();
        
        // Determine the target dictionary type
        // If target is IReadOnlyDictionary, use Dictionary (FrozenDictionary could be added for .NET 8+)
        sb.Append("new global::System.Collections.Generic.Dictionary<");
        sb.Append(GetTypeName(keyType));
        sb.Append(", ");
        sb.Append(GetTypeName(valueType));
        sb.Append("> { ");

        var first = true;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append("{ ");
            sb.Append(Serialize(entry.Key, keyType));
            sb.Append(", ");
            sb.Append(Serialize(entry.Value, valueType));
            sb.Append(" }");
        }

        sb.Append(" }");
        return sb.ToString();
    }

    /// <summary>
    /// Gets the C# type name for a given Type.
    /// </summary>
    public static string GetTypeName(Type type)
    {
        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(char)) return "char";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";

        if (type.IsArray)
        {
            return GetTypeName(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();
            var argNames = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                argNames[i] = GetTypeName(args[i]);
            }

            var baseName = genericDef.FullName ?? genericDef.Name;
            var backtickIndex = baseName.IndexOf('`');
            if (backtickIndex > 0)
            {
                baseName = baseName.Substring(0, backtickIndex);
            }

            return $"global::{baseName}<{string.Join(", ", argNames)}>";
        }

        return $"global::{type.FullName ?? type.Name}";
    }

    /// <summary>
    /// Gets the C# type name from a Roslyn type symbol display string.
    /// </summary>
    public static string GetTypeNameFromSymbol(string symbolDisplayString)
    {
        // Handle common type aliases
        return symbolDisplayString switch
        {
            "bool" => "bool",
            "byte" => "byte",
            "sbyte" => "sbyte",
            "short" => "short",
            "ushort" => "ushort",
            "int" => "int",
            "uint" => "uint",
            "long" => "long",
            "ulong" => "ulong",
            "float" => "float",
            "double" => "double",
            "decimal" => "decimal",
            "char" => "char",
            "string" => "string",
            "object" => "object",
            _ => symbolDisplayString.StartsWith("global::") ? symbolDisplayString : $"global::{symbolDisplayString}"
        };
    }

    private static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        var interfaceTypes = givenType.GetInterfaces();
        foreach (var it in interfaceTypes)
        {
            if (it.IsGenericType && it.GetGenericTypeDefinition() == genericType)
                return true;
        }

        if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericType)
            return true;

        var baseType = givenType.BaseType;
        if (baseType == null) return false;

        return IsAssignableToGenericType(baseType, genericType);
    }

    /// <summary>
    /// Checks if a type can be serialized to C#.
    /// </summary>
    public static bool CanSerialize(Type type)
    {
        // Check primitive types
        if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) || type == typeof(int) ||
            type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
            type == typeof(float) || type == typeof(double) || type == typeof(decimal) ||
            type == typeof(char) || type == typeof(string))
        {
            return true;
        }

        // Arrays are not allowed as return types because they are not immutable
        if (type.IsArray)
        {
            return false;
        }

        // Check generic collections
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var genericArgs = type.GetGenericArguments();

            // List<T>, IList<T>, IReadOnlyList<T>
            if (genericDef == typeof(List<>) ||
                genericDef == typeof(IList<>) ||
                genericDef == typeof(IReadOnlyList<>) ||
                genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IReadOnlyCollection<>))
            {
                return CanSerialize(genericArgs[0]);
            }

            // Dictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
            if (genericDef == typeof(Dictionary<,>) ||
                genericDef == typeof(IDictionary<,>) ||
                genericDef == typeof(IReadOnlyDictionary<,>))
            {
                return CanSerialize(genericArgs[0]) && CanSerialize(genericArgs[1]);
            }
        }

        return false;
    }
}
