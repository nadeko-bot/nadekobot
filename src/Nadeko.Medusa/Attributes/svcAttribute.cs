namespace NadekoBot.Medusa;

/// <summary>
/// Marks the class as a service which can be used within the same Medusa
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class svcAttribute : Attribute
{
    public Lifetime Lifetime { get; }
    public svcAttribute(Lifetime lifetime)
    {
        Lifetime = lifetime;
    }
}

/// <summary>
/// Lifetime for <see cref="svcAttribute"/>
/// </summary>
public enum Lifetime
{
    Singleton,
    Transient
}
