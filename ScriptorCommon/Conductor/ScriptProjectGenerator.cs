using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Scripts.Scriptor.Conductor
{
    public sealed record ScriptProjectGenerationResult(
        bool Succeeded,
        string ProjectPath,
        string? SolutionPath,
        IReadOnlyList<string> Messages);

    public static class ScriptProjectGenerator
    {
        public static ScriptProjectGenerationResult EnsureScriptProject(
            string scriptsRoot,
            IReadOnlyList<ScriptPackageDependency> dependencies,
            string? commonProjectPath = null,
            string? commonAssemblyPath = null,
            string solutionName = "ScriptorScripts")
        {
            if (string.IsNullOrWhiteSpace(scriptsRoot))
            {
                throw new ArgumentException("Scripts root cannot be null or whitespace.", nameof(scriptsRoot));
            }

            var messages = new List<string>();
            var projectRoot = Path.Combine(scriptsRoot, ".scriptor", "ScriptProject");
            Directory.CreateDirectory(projectRoot);

            var projectPath = Path.Combine(projectRoot, "ScriptorScripts.csproj");
            WriteProject(projectPath, dependencies, commonProjectPath, commonAssemblyPath);
            messages.Add($"Generated project at {projectPath}.");

            string? solutionPath = null;
            try
            {
                solutionPath = EnsureSolution(projectRoot, projectPath, solutionName, messages);
            }
            catch (Exception ex)
            {
                messages.Add($"Failed to generate solution: {ex.Message}");
            }

            return new ScriptProjectGenerationResult(true, projectPath, solutionPath, messages);
        }

        private static void WriteProject(
            string projectPath,
            IReadOnlyList<ScriptPackageDependency> dependencies,
            string? commonProjectPath,
            string? commonAssemblyPath)
        {
            var builder = new StringBuilder();
            builder.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
            builder.AppendLine("  <PropertyGroup>");
            builder.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
            builder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
            builder.AppendLine("    <Nullable>enable</Nullable>");
            builder.AppendLine("    <LangVersion>preview</LangVersion>");
            builder.AppendLine("  </PropertyGroup>");

            if (!string.IsNullOrWhiteSpace(commonProjectPath))
            {
                builder.AppendLine("  <ItemGroup>");
                builder.AppendLine($"    <ProjectReference Include=\"{Escape(commonProjectPath)}\" />");
                builder.AppendLine("  </ItemGroup>");
            }
            else if (!string.IsNullOrWhiteSpace(commonAssemblyPath))
            {
                builder.AppendLine("  <ItemGroup>");
                builder.AppendLine("    <Reference Include=\"ScriptorCommon\">");
                builder.AppendLine($"      <HintPath>{Escape(commonAssemblyPath)}</HintPath>");
                builder.AppendLine("    </Reference>");
                builder.AppendLine("  </ItemGroup>");
            }

            if (dependencies.Count > 0)
            {
                builder.AppendLine("  <ItemGroup>");
                foreach (var dependency in dependencies.OrderBy(d => d.PackageId))
                {
                    if (string.IsNullOrWhiteSpace(dependency.PackageId))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(dependency.Version))
                    {
                        builder.AppendLine($"    <PackageReference Include=\"{Escape(dependency.PackageId)}\" />");
                    }
                    else
                    {
                        builder.AppendLine($"    <PackageReference Include=\"{Escape(dependency.PackageId)}\" Version=\"{Escape(dependency.Version!)}\" />");
                    }
                }

                builder.AppendLine("  </ItemGroup>");
            }

            builder.AppendLine("</Project>");
            File.WriteAllText(projectPath, builder.ToString());
        }

        private static string? EnsureSolution(
            string projectRoot,
            string projectPath,
            string solutionName,
            ICollection<string> messages)
        {
            var solutionPath = Path.Combine(projectRoot, $"{solutionName}.sln");
            if (!File.Exists(solutionPath))
            {
                RunDotNetCommand(projectRoot, $"new sln -n {solutionName}", messages);
            }

            RunDotNetCommand(projectRoot, $"sln {solutionPath} add \"{projectPath}\"", messages);
            return solutionPath;
        }

        private static void RunDotNetCommand(string workingDirectory, string arguments, ICollection<string> messages)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                messages.Add($"Failed to run dotnet {arguments}.");
                return;
            }

            process.WaitForExit();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (!string.IsNullOrWhiteSpace(output))
            {
                messages.Add(output.Trim());
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                messages.Add(error.Trim());
            }
        }

        private static string Escape(string value) => value.Replace("\"", "&quot;");
    }
}
