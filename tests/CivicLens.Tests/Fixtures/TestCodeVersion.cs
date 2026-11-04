using System.Reflection;
using System.Reflection.Emit;
using CivicLens.Application;

namespace CivicLens.Tests.Fixtures;

internal static class TestCodeVersion
{
    public static readonly string Sha = new('c', 40);

    public static readonly CodeVersion Current = CodeVersion.FromAssembly(AssemblyVersioned($"1.0.0+{Sha}"));

    public static AssemblyBuilder AssemblyVersioned(string informationalVersion) =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("versioned"), AssemblyBuilderAccess.Run,
        [
            new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informationalVersion])
        ]);
}
