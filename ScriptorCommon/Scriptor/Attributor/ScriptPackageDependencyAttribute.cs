using System;

namespace Scripts.Scriptor.Attributor
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class ScriptPackageDependencyAttribute : Attribute
    {
        public ScriptPackageDependencyAttribute(string packageId, string? version = null)
        {
            PackageId = packageId;
            Version = version;
        }

        public string PackageId { get; }

        public string? Version { get; }
    }
}
