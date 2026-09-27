// Eazfuscator made every SpaceChem type `internal`. The module compiles against a publicized copy of
// the deob exe (tools/Publicize); this attribute makes the .NET Framework CLR skip the visibility
// checks at JIT time so the same references bind to the internal members of the live game.
// Verified on .NET Framework 4.8 with a two-assembly experiment (internal type + private method).
[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("SpaceChem")]

namespace System.Runtime.CompilerServices
{
    /// <summary>Recognized by the CLR by name; the BCL does not ship it for .NET Framework, so the
    /// consuming assembly declares it itself.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }

        public string AssemblyName { get; }
    }
}
