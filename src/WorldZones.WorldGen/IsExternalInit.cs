// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Polyfill for C# 9.0 init-only properties.
    /// Required for .NET Framework 4.7.2 / Unity 2019.4 compatibility.
    /// </summary>
    /// <remarks>
    /// This internal type allows the compiler to emit init-only setters that work
    /// with older runtimes lacking the IsExternalInit attribute.
    /// 
    /// See: https://github.com/CorundumGames/IsExternalInit
    /// </remarks>
    internal static class IsExternalInit
    {
    }
}
