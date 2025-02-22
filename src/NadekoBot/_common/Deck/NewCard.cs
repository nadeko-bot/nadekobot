namespace Nadeko.Econ;

public abstract record class NewCard<TSuit, TValue>(TSuit Suit, TValue Value)
    where TSuit : struct, Enum
    where TValue : struct, Enum;