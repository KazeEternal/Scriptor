using System;
using System.Collections.Generic;
using System.Reflection;

namespace Scripts.Scriptor.Conductor
{
    public sealed record ScriptParameterDescriptor(
        string Name,
        Type ParameterType,
        string? DisplayName,
        string? Description,
        string? Usage,
        object? DefaultValue);

    public sealed record ScriptRoutineDescriptor(
        string Name,
        string? Description,
        MethodInfo Method,
        IReadOnlyList<ScriptParameterDescriptor> Parameters);

    public static partial class ScriptRoutineDescriptorExtensions
    {
        public static string ToDisplayString(this ScriptRoutineDescriptor r) => r.Name;
    }

    public sealed record ScriptCollectionDescriptor(
        string Name,
        string? Description,
        Type CollectionType,
        IReadOnlyList<ScriptRoutineDescriptor> Routines);

    public sealed record ScriptPackageDependency(string PackageId, string? Version);

    public sealed record ScriptRuntimeSnapshot(
        IReadOnlyList<ScriptCollectionDescriptor> Collections,
        IReadOnlyList<ScriptPackageDependency> PackageDependencies);

    public sealed record ScriptExecutionResult(
        bool IsSuccess,
        IScriptContext Context,
        Exception? Exception,
        TimeSpan Duration);

    public sealed record ScriptCompilationDiagnostic(
        string Id,
        string Message,
        string Severity,
        string? FilePath,
        int? Line,
        int? Column);

    public sealed record ScriptCompilationResult(
        bool Succeeded,
        IReadOnlyList<ScriptCompilationDiagnostic> Diagnostics,
        string AssemblyPath);
}
