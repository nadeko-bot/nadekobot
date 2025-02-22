#nullable disable
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectGraphVisitors;

namespace NadekoBot.Common.Yml;

public class CommentsObjectGraphVisitor : ChainedObjectGraphVisitor
{
    public CommentsObjectGraphVisitor(IObjectGraphVisitor<IEmitter> nextVisitor)
        : base(nextVisitor)
    {
    }

    public override bool EnterMapping(IPropertyDescriptor key, IObjectDescriptor value, IEmitter context)
    {
        if (value is CommentsObjectDescriptor commentsDescriptor
            && !string.IsNullOrWhiteSpace(commentsDescriptor.Comment))
        {
            var parts = commentsDescriptor.Comment.Split('\n');

            foreach (var part in parts)
                context.Emit(new Comment(part.Trim(), false));
        }

        return base.EnterMapping(key, value, context);
    }
}