namespace Nadeko.Common;

public class EventPubSub : IPubSub
{
    private readonly Dictionary<string, Dictionary<Delegate, List<Func<object, ValueTask>>>> _actions = new();
    private readonly object _locker = new();

    public Task Sub<TData>(in TypedKey<TData> key, Func<TData, ValueTask> action)
        where TData : notnull
    {
        Func<object, ValueTask> localAction = obj => action((TData)obj);
        lock (_locker)
        {
            if (!_actions.TryGetValue(key.Key, out var keyActions))
            {
                keyActions = new();
                _actions[key.Key] = keyActions;
            }

            if (!keyActions.TryGetValue(action, out var sameActions))
            {
                sameActions = new();
                keyActions[action] = sameActions;
            }

            sameActions.Add(localAction);

            return Task.CompletedTask;
        }
    }

    public Task Pub<TData>(in TypedKey<TData> key, TData data)
        where TData : notnull
    {
        lock (_locker)
        {
            if (_actions.TryGetValue(key.Key, out var actions))
                // if this class ever gets used, this needs to be properly implemented
                // 1. ignore all valuetasks which are completed
                // 2. run all other tasks in parallel
                return actions.SelectMany(kvp => kvp.Value).Select(action => action(data).AsTask()).WhenAll();

            return Task.CompletedTask;
        }
    }

    public Task Unsub<TData>(in TypedKey<TData> key, Func<TData, ValueTask> action)
    {
        lock (_locker)
        {
            // get subscriptions for this action
            if (_actions.TryGetValue(key.Key, out var actions))
                // get subscriptions which have the same action hash code
                // note: having this as a list allows for multiple subscriptions of
                //       the same insance's/static method
            {
                if (actions.TryGetValue(action, out var sameActions))
                {
                    // remove last subscription
                    sameActions.RemoveAt(sameActions.Count - 1);

                    // if the last subscription was the only subscription
                    // we can safely remove this action's dictionary entry
                    if (sameActions.Count == 0)
                    {
                        actions.Remove(action);

                        // if our dictionary has no more elements after 
                        // removing the entry
                        // it's safe to remove it from the key's subscriptions
                        if (actions.Count == 0)
                            _actions.Remove(key.Key);
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}