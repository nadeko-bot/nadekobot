#nullable disable
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace NadekoBot.Common.Yml;

public class MultilineScalarFlowStyleEmitter : ChainedEventEmitter
{
    public MultilineScalarFlowStyleEmitter(IEventEmitter nextEmitter)
        : base(nextEmitter)
    {
    }

    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if (typeof(string).IsAssignableFrom(eventInfo.Source.Type))
        {
            var value = eventInfo.Source.Value as string;
            if (!string.IsNullOrEmpty(value))
            {
                var isMultiLine = value.IndexOfAny(['\r', '\n', '\x85', '\x2028', '\x2029']) >= 0;
                if (isMultiLine)
                {
                    eventInfo = new(eventInfo.Source)
                    {
                        Style = ScalarStyle.Literal
                    };
                }
            }
        }

        nextEmitter.Emit(eventInfo, emitter);
    }
}