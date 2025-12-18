using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynCompilation = Microsoft.CodeAnalysis.Compilation;

namespace Comptime;

/// <summary>
/// Incremental source generator that executes methods marked with [Comptime] at compile time
/// and generates C# code with the serialized results.
///
/// It looks for static methods annotated with <see cref="ComptimeAttribute"/> that:
///   - are static
///   - are parameterless
///   - return a serializable type
/// It then:
///   - finds all invocation sites of these methods,
///   - builds a temporary compilation including those methods,
///   - executes the methods to obtain their return values,
///   - serializes those values to C# code,
///   - and emits interceptor methods that return the pre-computed values.
/// </summary>
[Generator]
public sealed class ComptimeSourceGenerator : IIncrementalGenerator
{
    #region Diagnostic Descriptors

    private static readonly DiagnosticDescriptor ClassNotPartialDescriptor = new(
        "COMPTIME001",
        "Class must be partial",
        "Class '{0}' containing [Comptime] method '{1}' must be declared as partial",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes containing [Comptime] methods must be declared as partial so that the source generator can add generated code to the class.");

    private static readonly DiagnosticDescriptor MethodNotStaticDescriptor = new(
        "COMPTIME002",
        "Method must be static",
        "[Comptime] method '{0}' must be static",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods marked with [Comptime] must be static.");

    private static readonly DiagnosticDescriptor UnsupportedReturnTypeDescriptor = new(
        "COMPTIME004",
        "Unsupported return type",
        "[Comptime] method '{0}' has return type '{1}' which cannot be serialized to C#",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods marked with [Comptime] must return a type that can be serialized to C#.");

    private static readonly DiagnosticDescriptor ArrayReturnTypeNotAllowedDescriptor = new(
        "COMPTIME011",
        "Array return type not allowed",
        "[Comptime] method '{0}' returns an array type '{1}'. Arrays are not allowed because they are mutable; use IReadOnlyList<T> instead.",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods marked with [Comptime] must not return array types because arrays are mutable. Use IReadOnlyList<T> instead.");

    private static readonly DiagnosticDescriptor ArgumentMustBeConstantDescriptor = new(
        "COMPTIME012",
        "Argument must be a constant",
        "Argument '{0}' to [Comptime] method '{1}' must be a compile-time constant literal or an expression of literals. Variables, method calls, and other non-constant expressions are not allowed.",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Arguments to [Comptime] methods must be compile-time constant literals or expressions of literals. Variables, method calls, loops, and other non-constant expressions cannot be evaluated at compile time.");

    private static readonly DiagnosticDescriptor GenerationSucceededDescriptor = new(
        "COMPTIME000",
        "Compile-time execution succeeded",
        "Successfully generated source for method '{0}' with {1} intercepted call site(s)",
        "Comptime",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The source generator successfully executed and generated code for this method.");

    private static readonly DiagnosticDescriptor EmitFailedDescriptor = new(
        "COMPTIME005",
        "Compilation emit failed",
        "Emit failed for method '{0}': {1}",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ExecutionFailedDescriptor = new(
        "COMPTIME006",
        "Method execution failed",
        "[Comptime] method '{0}' threw an exception during compile-time execution: {1}",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SerializationFailedDescriptor = new(
        "COMPTIME007",
        "Serialization failed",
        "Could not serialize return value of method '{0}': {1}",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor IncludeFileNotFoundDescriptor = new(
        "COMPTIME008",
        "Include file not found",
        "Could not find included file '{0}' for method '{1}'",
        "Comptime",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MethodNotFoundDescriptor = new(
        "COMPTIME009",
        "Method not found in emitted assembly",
        "Could not find method '{0}' on type '{1}'",
        "Comptime",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenerationErrorDescriptor = new(
        "COMPTIME010",
        "Error generating source",
        "An error occurred while generating source for method '{0}': {1}",
        "Comptime",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    #endregion


    /// <summary>
    /// Initializes the incremental generator to find and process methods annotated with <see cref="ComptimeAttribute"/>.
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Find candidate methods syntactically (methods with attributes).
        var methodDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => IsCandidateMethod(node),
            static (syntaxContext, _) => GetMethodToGenerate(syntaxContext))
            .Where(static m => m is not null)!;

        // 2. Find invocations of methods (potential calls to [Comptime] methods).
        var invocations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax,
            static (syntaxContext, ct) => GetInvocationToIntercept(syntaxContext, ct))
            .Where(static i => i is not null)!;

        // 3. Capture target framework information for the current compilation.
        var targetFramework = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.TargetFramework", out var tfm);
                options.GlobalOptions.TryGetValue("build_property.TargetFrameworkIdentifier", out var identifier);
                options.GlobalOptions.TryGetValue("build_property.TargetFrameworkVersion", out var version);

                return (tfm: tfm ?? "", identifier: identifier ?? "", version: version ?? "");
            });

        // 4. Combine the collected methods with invocations, Compilation, and TFM.
        var combinedData = context.CompilationProvider
            .Combine(targetFramework)
            .Combine(methodDeclarations.Collect())
            .Combine(invocations.Collect());

        // 5. Register for source output.
        context.RegisterSourceOutput(combinedData, static (spc, source) =>
        {
            var (((compilation, tfmInfo), methods), invocationList) = source;
            
            // Output debug info
            spc.AddSource("ComptimeDebugInfo.g.cs", SourceText.From(
                "// <auto-generated />\n" +
                "// TargetFramework: " + (string.IsNullOrEmpty(tfmInfo.tfm) ? "<unknown>" : tfmInfo.tfm) + "\n" +
                "// Methods count: " + (methods.IsDefaultOrEmpty ? "0" : methods.Length.ToString(CultureInfo.InvariantCulture)) + "\n" +
                "// Invocations count: " + (invocationList.IsDefaultOrEmpty ? "0" : invocationList.Length.ToString(CultureInfo.InvariantCulture)) + "\n" +
                "namespace Comptime.Internal;\n" +
                "internal static class DebugInfo { }\n", Encoding.UTF8));

            if (methods.IsDefaultOrEmpty)
            {
                return;
            }

            // Build a lookup of method symbols to their invocations
            var invocationsByMethod = new Dictionary<string, List<InvocationInfo>>(StringComparer.Ordinal);
            if (!invocationList.IsDefaultOrEmpty)
            {
                foreach (var inv in invocationList)
                {
                    if (inv is null) continue;
                    var key = inv.Value.TargetMethodKey;
                    if (!invocationsByMethod.TryGetValue(key, out var list))
                    {
                        list = new List<InvocationInfo>();
                        invocationsByMethod[key] = list;
                    }
                    list.Add(inv.Value);
                }
            }

            foreach (var m in methods)
            {
                if (m is null)
                {
                    continue;
                }

                // Report any validation errors first
                if (m.Value.ValidationErrors is not null && m.Value.ValidationArgs is not null)
                {
                    for (int i = 0; i < m.Value.ValidationErrors.Length; i++)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            m.Value.ValidationErrors[i],
                            m.Value.AttributeLocation ?? m.Value.Method.Locations.FirstOrDefault(),
                            m.Value.ValidationArgs[i]));
                    }
                    // Skip generation if there are validation errors
                    continue;
                }

                try
                {
                    // Get invocations for this method
                    var methodKey = GetMethodKey(m.Value.Method);
                    invocationsByMethod.TryGetValue(methodKey, out var methodInvocations);
                    
                    // Report errors for invocations with non-constant arguments and filter them out
                    var validInvocations = new List<InvocationInfo>();
                    if (methodInvocations is not null)
                    {
                        foreach (var inv in methodInvocations)
                        {
                            if (inv.ErrorMessage is not null)
                            {
                                spc.ReportDiagnostic(Diagnostic.Create(
                                    ArgumentMustBeConstantDescriptor,
                                    inv.Location,
                                    inv.ErrorMessage,
                                    m.Value.Method.Name));
                            }
                            else
                            {
                                validInvocations.Add(inv);
                            }
                        }
                    }

                    GenerateForMethod(spc, compilation, tfmInfo, m.Value, validInvocations);
                }
                catch (Exception ex)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        GenerationErrorDescriptor,
                        m.Value.Method.Locations.FirstOrDefault(),
                        m.Value.Method.Name,
                        ex.Message));
                    continue;
                }
            }
        });
    }

    private static string GetMethodKey(IMethodSymbol method)
    {
        return method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;
    }


    private static InvocationInfo? GetInvocationToIntercept(GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        
        // Get the method being called
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        // Check if the method has [Comptime] attribute
        var comptimeAttrSymbol = semanticModel.Compilation.GetTypeByMetadataName("Comptime.ComptimeAttribute");
        if (comptimeAttrSymbol is null)
        {
            return null;
        }

        var hasComptimeAttr = methodSymbol.GetAttributes()
            .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, comptimeAttrSymbol));
        
        if (!hasComptimeAttr)
        {
            return null;
        }

        // Collect argument expressions (must not contain any variable references)
        var argumentExpressions = new List<string>();
        string? errorMessage = null;
        
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            // Check if the expression contains any variable or parameter references
            var variableReference = FindVariableReference(arg.Expression, semanticModel, ct);
            if (variableReference is not null)
            {
                // Expression contains a variable - cannot be evaluated at compile time
                errorMessage = variableReference;
                break;
            }
            
            // Capture the expression source text for compilation
            argumentExpressions.Add(arg.Expression.ToFullString().Trim());
        }

        // If there's an error, return an InvocationInfo with the error
        if (errorMessage is not null)
        {
            var methodKey = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + methodSymbol.Name;
            return new InvocationInfo(
                methodKey,
                null,
                invocation.GetLocation(),
                Array.Empty<string>(),
                errorMessage);
        }

        // Get the interceptable location using Roslyn's API
        var interceptableLocation = semanticModel.GetInterceptableLocation(invocation, ct);
        if (interceptableLocation is null)
        {
            return null;
        }

        var methodKeySuccess = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + methodSymbol.Name;
        
        return new InvocationInfo(
            methodKeySuccess,
            interceptableLocation.GetInterceptsLocationAttributeSyntax(),
            invocation.GetLocation(),
            argumentExpressions.ToArray(),
            null);
    }

    /// <summary>
    /// Recursively checks if an expression contains any variable or parameter references.
    /// Returns the problematic identifier if found, null otherwise.
    /// </summary>
    private static string? FindVariableReference(ExpressionSyntax expression, SemanticModel semanticModel, System.Threading.CancellationToken ct)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is IdentifierNameSyntax identifier)
            {
                var symbol = semanticModel.GetSymbolInfo(identifier, ct).Symbol;
                
                // Check if it's a variable, parameter, or field that's not a constant
                if (symbol is ILocalSymbol || symbol is IParameterSymbol)
                {
                    return identifier.Identifier.Text;
                }
                
                if (symbol is IFieldSymbol field && !field.IsConst)
                {
                    return identifier.Identifier.Text;
                }
                
                // Allow type names, method names, const fields, enum members, etc.
            }
        }
        
        return null;
    }

    private static Type? GetRuntimeType(ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType switch
        {
            SpecialType.System_Boolean => typeof(bool),
            SpecialType.System_Byte => typeof(byte),
            SpecialType.System_SByte => typeof(sbyte),
            SpecialType.System_Int16 => typeof(short),
            SpecialType.System_UInt16 => typeof(ushort),
            SpecialType.System_Int32 => typeof(int),
            SpecialType.System_UInt32 => typeof(uint),
            SpecialType.System_Int64 => typeof(long),
            SpecialType.System_UInt64 => typeof(ulong),
            SpecialType.System_Single => typeof(float),
            SpecialType.System_Double => typeof(double),
            SpecialType.System_Decimal => typeof(decimal),
            SpecialType.System_Char => typeof(char),
            SpecialType.System_String => typeof(string),
            _ => null
        };
    }

    private static bool IsCandidateMethod(SyntaxNode node)
        => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0;

    private static MethodToGenerate? GetMethodToGenerate(GeneratorSyntaxContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
        if (methodSymbol is null)
        {
            return null;
        }

        var compilation = semanticModel.Compilation;
        var comptimeAttrSymbol = compilation.GetTypeByMetadataName("Comptime.ComptimeAttribute");
        if (comptimeAttrSymbol is null)
        {
            return null;
        }

        // Check if method has [Comptime] attribute
        var hasAttribute = methodSymbol.GetAttributes()
            .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, comptimeAttrSymbol));

        if (!hasAttribute)
        {
            return null;
        }

        var attrLocation = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, comptimeAttrSymbol))
            ?.ApplicationSyntaxReference?.GetSyntax().GetLocation();

        // Collect validation errors
        var validationErrors = new List<DiagnosticDescriptor>();
        var validationArgs = new List<object?[]>();

        // Check if class is partial
        var containingType = methodSymbol.ContainingType;
        var typeDeclaration = containingType.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();
        
        if (typeDeclaration is not null && !typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            validationErrors.Add(ClassNotPartialDescriptor);
            validationArgs.Add(new object?[] { containingType.Name, methodSymbol.Name });
        }

        // Check if method is static
        if (!methodSymbol.IsStatic)
        {
            validationErrors.Add(MethodNotStaticDescriptor);
            validationArgs.Add(new object?[] { methodSymbol.Name });
        }

        // Check for [IncludeFiles] attribute on method and containing class
        var includeFilesAttrSymbol = compilation.GetTypeByMetadataName("Comptime.IncludeFilesAttribute");
        var additionalFiles = new List<string>();
        if (includeFilesAttrSymbol is not null)
        {
            CollectAttributeArrayValues(containingType, includeFilesAttrSymbol, additionalFiles);
            CollectAttributeArrayValues(methodSymbol, includeFilesAttrSymbol, additionalFiles);
        }

        // Check for [IncludeUsings] attribute on method and containing class
        var includeUsingsAttrSymbol = compilation.GetTypeByMetadataName("Comptime.IncludeUsingsAttribute");
        var additionalUsings = new List<string>();
        if (includeUsingsAttrSymbol is not null)
        {
            CollectAttributeArrayValues(containingType, includeUsingsAttrSymbol, additionalUsings);
            CollectAttributeArrayValues(methodSymbol, includeUsingsAttrSymbol, additionalUsings);
        }

        // Check for [IncludeGenerators] attribute on method and containing class
        var includeGeneratorsAttrSymbol = compilation.GetTypeByMetadataName("Comptime.IncludeGeneratorsAttribute");
        var additionalGenerators = new List<string>();
        if (includeGeneratorsAttrSymbol is not null)
        {
            CollectAttributeArrayValues(containingType, includeGeneratorsAttrSymbol, additionalGenerators);
            CollectAttributeArrayValues(methodSymbol, includeGeneratorsAttrSymbol, additionalGenerators);
        }

        return new MethodToGenerate(
            methodSymbol, 
            attrLocation, 
            additionalFiles.ToArray(), 
            additionalUsings.ToArray(),
            additionalGenerators.ToArray(),
            validationErrors.Count > 0 ? validationErrors.ToArray() : null,
            validationErrors.Count > 0 ? validationArgs.ToArray() : null);
    }

    private static void CollectAttributeArrayValues(ISymbol symbol, INamedTypeSymbol attributeType, List<string> values)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));
        
        if (attr is not null && attr.ConstructorArguments.Length > 0)
        {
            var arg = attr.ConstructorArguments[0];
            if (arg.Kind == TypedConstantKind.Array)
            {
                values.AddRange(arg.Values
                    .Where(v => v.Value is string)
                    .Select(v => (string)v.Value!));
            }
        }
    }

    private readonly record struct MethodToGenerate(
        IMethodSymbol Method, 
        Location? AttributeLocation, 
        string[] AdditionalFiles,
        string[] AdditionalUsings,
        string[] AdditionalGenerators,
        DiagnosticDescriptor[]? ValidationErrors,
        object?[][]? ValidationArgs);
    
    private readonly record struct InvocationInfo(
        string TargetMethodKey, 
        string? InterceptsLocationAttribute, 
        Location Location,
        string[] ArgumentExpressions,
        string? ErrorMessage);

    [SuppressMessage("Build", "RS1035", Justification = "The generator must execute methods to produce source output.")]
    private static void GenerateForMethod(
        SourceProductionContext context,
        RoslynCompilation hostCompilation,
        (string tfm, string identifier, string version) tfmInfo,
        MethodToGenerate methodInfo,
        List<InvocationInfo> invocations)
    {
        var methodSymbol = methodInfo.Method;

        // Get the syntax tree containing this method
        var methodSyntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (methodSyntaxRef is null)
        {
            return;
        }
        var originalSyntaxTree = methodSyntaxRef.SyntaxTree;
        var methodSyntax = methodSyntaxRef.GetSyntax() as MethodDeclarationSyntax;
        
        if (methodSyntax is null)
        {
            return;
        }

        var parseOptions = (CSharpParseOptions)originalSyntaxTree.Options;

        // Create compilation for execution
        var tempCompilation = hostCompilation;

        // Run additional source generators if specified via [IncludeGenerators] attribute
        if (methodInfo.AdditionalGenerators.Length > 0)
        {
            tempCompilation = RunAdditionalGenerators(context, tempCompilation, methodInfo.AdditionalGenerators, parseOptions, methodSymbol);
        }

        using var peStream = new MemoryStream();
        var emitResult = tempCompilation.Emit(peStream);

        if (!emitResult.Success)
        {
            var errorMessages = string.Join("; ", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(5)
                .Select(d => 
                {
                    var location = d.Location;
                    var lineSpan = location.GetLineSpan();
                    var fileName = Path.GetFileName(lineSpan.Path);
                    var line = lineSpan.StartLinePosition.Line + 1;
                    return $"{fileName}({line}): {d.GetMessage(CultureInfo.InvariantCulture)}";
                }));
            
            context.ReportDiagnostic(Diagnostic.Create(
                EmitFailedDescriptor,
                methodSymbol.Locations.FirstOrDefault(), 
                methodSymbol.Name, 
                errorMessages));
            return;
        }

        peStream.Position = 0;

        // Build a dictionary of assembly paths from the compilation's references
        var assemblyPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pendingCompilationRefs = new List<(string Name, RoslynCompilation Compilation)>();
        var loadedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in hostCompilation.References)
        {
            if (reference is PortableExecutableReference peRef && peRef.FilePath is not null)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(peRef.FilePath);
                var filePath = peRef.FilePath;
                
                // Handle ref assemblies - try to find actual assembly
                if (filePath.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar) ||
                    filePath.Contains("/ref/"))
                {
                    var objIndex = filePath.LastIndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    if (objIndex < 0) objIndex = filePath.LastIndexOf("/obj/", StringComparison.OrdinalIgnoreCase);
                    
                    if (objIndex >= 0)
                    {
                        var baseDir = filePath.Substring(0, objIndex);
                        var afterObj = filePath.Substring(objIndex + 4);
                        var refIndex = afterObj.IndexOf(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                        if (refIndex < 0) refIndex = afterObj.IndexOf("/ref/", StringComparison.OrdinalIgnoreCase);
                        
                        if (refIndex >= 0)
                        {
                            var configTfm = afterObj.Substring(0, refIndex);
                            var fileName = Path.GetFileName(filePath);
                            var binPath = baseDir + Path.DirectorySeparatorChar + "bin" + configTfm + Path.DirectorySeparatorChar + fileName;
                            
                            if (File.Exists(binPath))
                            {
                                filePath = binPath;
                            }
                        }
                    }
                }
                
                if (!assemblyPaths.ContainsKey(assemblyName))
                {
                    assemblyPaths[assemblyName] = filePath;
                }
            }
            else if (reference is CompilationReference compRef)
            {
                var refCompilation = compRef.Compilation;
                var refAssemblyName = refCompilation.AssemblyName;
                if (refAssemblyName is not null)
                {
                    pendingCompilationRefs.Add((refAssemblyName, refCompilation));
                }
            }
        }

        // Set up an assembly resolver
        ResolveEventHandler? resolver = null;
        resolver = (sender, args) =>
        {
            var requestedName = new AssemblyName(args.Name);

            // Try to return pre-loaded assembly from project references
            if (requestedName.Name is not null && loadedAssemblies.TryGetValue(requestedName.Name, out var loadedAssembly))
            {
                return loadedAssembly;
            }

            // Try to load from file-based references
            if (requestedName.Name is not null && assemblyPaths.TryGetValue(requestedName.Name, out var path))
            {
                try
                {
                    var asm = Assembly.LoadFrom(path);
                    loadedAssemblies[requestedName.Name] = asm;
                    return asm;
                }
                catch
                {
                    // Fall through to return null
                }
            }

            return null;
        };

        AppDomain.CurrentDomain.AssemblyResolve += resolver;

        // Load CompilationReference assemblies
        foreach (var (refAssemblyName, refCompilation) in pendingCompilationRefs)
        {
            if (!loadedAssemblies.ContainsKey(refAssemblyName))
            {
                using var refPeStream = new MemoryStream();
                var refEmitResult = refCompilation.Emit(refPeStream);
                if (refEmitResult.Success)
                {
                    refPeStream.Position = 0;
                    try
                    {
                        var refAssembly = Assembly.Load(refPeStream.ToArray());
                        loadedAssemblies[refAssemblyName] = refAssembly;
                    }
                    catch
                    {
                        // Ignore load failures
                    }
                }
            }
        }

        try
        {
            var assembly = Assembly.Load(peStream.ToArray());

            // Locate the generated type and method via reflection
            var containingTypeName = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            const string globalPrefix = "global::";
            if (containingTypeName.StartsWith(globalPrefix, StringComparison.Ordinal))
            {
                containingTypeName = containingTypeName.Substring(globalPrefix.Length);
            }

            var type = assembly.GetType(containingTypeName);
            if (type is null)
            {
                return;
            }

            // Find the method with matching parameter count
            var parameterTypes = methodSymbol.Parameters.Select(p => GetRuntimeType(p.Type)).ToArray();
            MethodInfo? method = null;
            
            if (parameterTypes.All(t => t is not null))
            {
                method = type.GetMethod(
                    methodSymbol.Name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    parameterTypes!,
                    null);
            }
            
            // Fallback to finding by name if exact match fails
            method ??= type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == methodSymbol.Name && m.GetParameters().Length == methodSymbol.Parameters.Length);

            if (method is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MethodNotFoundDescriptor,
                    methodSymbol.Locations.FirstOrDefault(), 
                    methodSymbol.Name, 
                    containingTypeName));
                return;
            }

            // Get the return type
            var returnType = method.ReturnType;

            // Check if the type is an array (not allowed because arrays are mutable)
            if (returnType.IsArray)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ArrayReturnTypeNotAllowedDescriptor,
                    methodInfo.AttributeLocation ?? methodSymbol.Locations.FirstOrDefault(),
                    methodSymbol.Name,
                    returnType.FullName));
                return;
            }

            // Check if the type can be serialized
            if (!CSharpSerializer.CanSerialize(returnType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedReturnTypeDescriptor,
                    methodInfo.AttributeLocation ?? methodSymbol.Locations.FirstOrDefault(),
                    methodSymbol.Name,
                    returnType.FullName));
                return;
            }

            // Group invocations by their argument expressions to avoid duplicate execution
            var invocationGroups = new List<(string ArgsKey, string[] ArgExpressions, List<InvocationInfo> Invocations)>();
            
            foreach (var inv in invocations)
            {
                // Create a key based on argument expressions
                var argsKey = string.Join("|", inv.ArgumentExpressions);
                
                var existingGroup = invocationGroups.FirstOrDefault(g => g.ArgsKey == argsKey);
                if (existingGroup.Invocations is not null)
                {
                    existingGroup.Invocations.Add(inv);
                }
                else
                {
                    invocationGroups.Add((argsKey, inv.ArgumentExpressions, new List<InvocationInfo> { inv }));
                }
            }

            // Execute method for each unique argument combination and collect results
            var executionResults = new List<(string SerializedValue, string[] ArgExpressions, List<InvocationInfo> Invocations)>();
            
            foreach (var (argsKey, argExpressions, groupInvocations) in invocationGroups)
            {
                object? result;
                try
                {
                    // For methods with arguments, we need to invoke with the evaluated expressions
                    if (argExpressions.Length > 0)
                    {
                        // Create a wrapper method that calls the original method with the literal expressions
                        var wrapperResult = ExecuteMethodWithArguments(
                            tempCompilation, 
                            parseOptions, 
                            methodSymbol, 
                            argExpressions, 
                            assemblyPaths, 
                            loadedAssemblies,
                            pendingCompilationRefs);
                        
                        if (wrapperResult.Error is not null)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                ExecutionFailedDescriptor,
                                groupInvocations[0].Location,
                                methodSymbol.Name,
                                wrapperResult.Error));
                            continue;
                        }
                        
                        result = wrapperResult.Result;
                    }
                    else
                    {
                        result = method.Invoke(null, null);
                    }
                }
                catch (TargetInvocationException ex)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ExecutionFailedDescriptor,
                        groupInvocations[0].Location,
                        methodSymbol.Name,
                        ex.InnerException?.Message ?? ex.Message));
                    continue;
                }
                catch (Exception ex)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ExecutionFailedDescriptor,
                        groupInvocations[0].Location,
                        methodSymbol.Name,
                        ex.Message));
                    continue;
                }

                // Serialize the result to C#
                if (!CSharpSerializer.TrySerialize(result, returnType, out var serializedValue, out var serializeError))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SerializationFailedDescriptor,
                        groupInvocations[0].Location,
                        methodSymbol.Name,
                        serializeError));
                    continue;
                }

                executionResults.Add((serializedValue, argExpressions, groupInvocations));
            }

            if (executionResults.Count == 0)
            {
                return;
            }

            // Generate the source code
            var sourceText = GenerateSourceCode(methodSymbol, returnType, executionResults, methodInfo.AdditionalUsings);

            var hintName = $"{methodSymbol.ContainingType.Name}_{methodSymbol.Name}.Comptime.g.cs";
            
            context.AddSource(hintName, SourceText.From(sourceText, Encoding.UTF8));
            
            // Report success
            context.ReportDiagnostic(Diagnostic.Create(
                GenerationSucceededDescriptor,
                methodInfo.AttributeLocation ?? methodSymbol.Locations.FirstOrDefault(),
                methodSymbol.Name,
                invocations.Count));
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    private static string GenerateSourceCode(
        IMethodSymbol methodSymbol,
        Type returnType,
        List<(string SerializedValue, string[] ArgExpressions, List<InvocationInfo> Invocations)> executionResults,
        string[] additionalUsings)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        
        foreach (var u in additionalUsings)
        {
            sb.AppendLine($"using {u};");
        }
        
        sb.AppendLine();

        // File-local InterceptsLocationAttribute to avoid conflicts with other generators
        sb.AppendLine("namespace System.Runtime.CompilerServices");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        sb.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
        sb.AppendLine("    {");
        sb.AppendLine("        public InterceptsLocationAttribute(int version, string data) { }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // Get namespace and class info
        var containingType = methodSymbol.ContainingType;
        var ns = containingType.ContainingNamespace;
        var hasNamespace = !ns.IsGlobalNamespace;

        if (hasNamespace)
        {
            sb.AppendLine($"namespace {ns.ToDisplayString()}");
            sb.AppendLine("{");
        }

        // Get accessibility modifier
        var accessibility = containingType.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal"
        };

        var indent = hasNamespace ? "    " : "";
        
        // Generate partial class
        sb.AppendLine($"{indent}{accessibility} static partial class {containingType.Name}");
        sb.AppendLine($"{indent}{{");

        var returnTypeName = CSharpSerializer.GetTypeName(returnType);
        var methodAccessibility = methodSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal"
        };

        // Build parameter list for interceptor methods using the symbol's type names
        var parameterList = string.Join(", ", methodSymbol.Parameters.Select(p => 
            $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}"));

        // Build type parameter list for generic methods
        var typeParameterList = methodSymbol.TypeParameters.Length > 0
            ? "<" + string.Join(", ", methodSymbol.TypeParameters.Select(tp => tp.Name)) + ">"
            : "";

        // Generate a field and interceptor for each unique argument combination
        var counter = 0;
        foreach (var (serializedValue, argExpressions, invocations) in executionResults)
        {
            var suffix = counter == 0 ? "" : $"_{counter}";
            var fieldName = $"_comptime_{methodSymbol.Name}{suffix}";
            
            // Generate the cached field
            sb.AppendLine($"{indent}    private static readonly {returnTypeName} {fieldName} = {serializedValue};");
            sb.AppendLine();

            // Generate interceptor method with InterceptsLocation attributes for this group
            foreach (var invocation in invocations)
            {
                sb.AppendLine($"{indent}    {invocation.InterceptsLocationAttribute}");
            }
            
            sb.AppendLine($"{indent}    {methodAccessibility} static {returnTypeName} {methodSymbol.Name}_Intercepted{suffix}{typeParameterList}({parameterList})");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        return {fieldName};");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine();
            
            counter++;
        }

        sb.AppendLine($"{indent}}}");

        if (hasNamespace)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    [SuppressMessage("Build", "RS1035", Justification = "The generator must execute methods to produce source output.")]
    private static (object? Result, string? Error) ExecuteMethodWithArguments(
        RoslynCompilation compilation,
        CSharpParseOptions parseOptions,
        IMethodSymbol methodSymbol,
        string[] argExpressions,
        Dictionary<string, string> assemblyPaths,
        Dictionary<string, Assembly> loadedAssemblies,
        List<(string Name, RoslynCompilation Compilation)> pendingCompilationRefs)
    {
        // Build the fully qualified method call
        var containingTypeName = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var argsString = string.Join(", ", argExpressions);
        
        // Create a wrapper class with a method that calls the target method with the literal arguments
        var wrapperCode = $@"
using System;
using System.Collections.Generic;
using System.Linq;

namespace ComptimeWrapper
{{
    public static class Wrapper
    {{
        public static object Execute()
        {{
            return {containingTypeName}.{methodSymbol.Name}({argsString});
        }}
    }}
}}
";
        
        var wrapperTree = CSharpSyntaxTree.ParseText(wrapperCode, parseOptions);
        var wrapperCompilation = compilation.AddSyntaxTrees(wrapperTree);
        
        using var peStream = new MemoryStream();
        var emitResult = wrapperCompilation.Emit(peStream);
        
        if (!emitResult.Success)
        {
            var errors = string.Join("; ", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3)
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture)));
            return (null, $"Failed to compile argument expressions: {errors}");
        }
        
        peStream.Position = 0;
        
        try
        {
            var assembly = Assembly.Load(peStream.ToArray());
            var wrapperType = assembly.GetType("ComptimeWrapper.Wrapper");
            if (wrapperType is null)
            {
                return (null, "Could not find wrapper type");
            }
            
            var executeMethod = wrapperType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
            if (executeMethod is null)
            {
                return (null, "Could not find Execute method");
            }
            
            var result = executeMethod.Invoke(null, null);
            return (result, null);
        }
        catch (TargetInvocationException ex)
        {
            return (null, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static RoslynCompilation RunAdditionalGenerators(
        SourceProductionContext context,
        RoslynCompilation compilation,
        string[] generatorAssemblies,
        CSharpParseOptions parseOptions,
        IMethodSymbol methodSymbol)
    {
        var generators = new List<ISourceGenerator>();

        foreach (var genAssemblyName in generatorAssemblies)
        {
            // Find the generator assembly in analyzer references
            foreach (var reference in compilation.References)
            {
                if (reference is PortableExecutableReference peRef && peRef.FilePath is not null)
                {
                    var refName = Path.GetFileNameWithoutExtension(peRef.FilePath);
                    if (string.Equals(refName, genAssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var genAssembly = Assembly.LoadFrom(peRef.FilePath);
                            var genTypes = genAssembly.GetTypes()
                                .Where(t => typeof(ISourceGenerator).IsAssignableFrom(t) && !t.IsAbstract);
                            
                            foreach (var genType in genTypes)
                            {
                                if (Activator.CreateInstance(genType) is ISourceGenerator gen)
                                {
                                    generators.Add(gen);
                                }
                            }
                        }
                        catch
                        {
                            // Ignore load failures
                        }
                    }
                }
            }
        }

        if (generators.Count > 0)
        {
            var driver = CSharpGeneratorDriver.Create(generators.ToArray())
                .WithUpdatedParseOptions(parseOptions);
            
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return updatedCompilation;
        }

        return compilation;
    }
}
