namespace Nadeko.Econ;

public static class RegularDeckExtensions
{
    public static string GetEmoji(this RegularSuit suit)
        => suit switch
        {
            RegularSuit.Hearts => "♥️",
            RegularSuit.Spades => "♠️",
            RegularSuit.Diamonds => "♦️",
            _ => "♣️",
        };

    public static string GetEmoji(this RegularValue value)
        => value switch
        {
            RegularValue.Ace => "🇦",
            RegularValue.Two => "2️⃣",
            RegularValue.Three => "3️⃣",
            RegularValue.Four => "4️⃣",
            RegularValue.Five => "5️⃣",
            RegularValue.Six => "6️⃣",
            RegularValue.Seven => "7️⃣",
            RegularValue.Eight => "8️⃣",
            RegularValue.Nine => "9️⃣",
            RegularValue.Ten => "🔟",
            RegularValue.Jack => "🇯",
            RegularValue.Queen => "🇶",
            _ => "🇰",
        };

    public static string GetEmoji(this RegularCard card)
        => $"{card.Value.GetEmoji()} {card.Suit.GetEmoji()}";

    public static string GetName(this RegularValue value)
        => value.ToString();

    public static string GetName(this RegularSuit suit)
        => suit.ToString();

    public static string GetName(this RegularCard card)
        => $"{card.Value.ToString()} of {card.Suit.GetName()}";
}













