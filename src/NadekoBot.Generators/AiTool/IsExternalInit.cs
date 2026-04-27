// Polyfill for `init`-only setters on netstandard2.0.
// The C# compiler will use this type to recognise the `init` accessor.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
