namespace NadekoBot.Extensions;

public static class LinkedListExtensions
{
    public static LinkedListNode<T>? FindNode<T>(this LinkedList<T> list, Func<T, bool> predicate)
    {
        var node = list.First;
        while (node is not null)
        {
            if (predicate(node.Value))
                return node;

            node = node.Next;
        }

        return null;
    }
}