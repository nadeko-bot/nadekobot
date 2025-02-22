#nullable disable
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.TypeInspectors;

namespace NadekoBot.Common.Yml;

public class CommentGatheringTypeInspector : TypeInspectorSkeleton
{
    private readonly ITypeInspector _innerTypeDescriptor;

    public CommentGatheringTypeInspector(ITypeInspector innerTypeDescriptor)
        => _innerTypeDescriptor = innerTypeDescriptor ?? throw new ArgumentNullException(nameof(innerTypeDescriptor));

    public override IEnumerable<IPropertyDescriptor> GetProperties(Type type, object container)
        => _innerTypeDescriptor.GetProperties(type, container).Select(d => new CommentsPropertyDescriptor(d));

    private sealed class CommentsPropertyDescriptor : IPropertyDescriptor
    {
        public string Name { get; }

        public Type Type
            => _baseDescriptor.Type;

        public Type TypeOverride
        {
            get => _baseDescriptor.TypeOverride;
            set => _baseDescriptor.TypeOverride = value;
        }

        public int Order { get; set; }

        public ScalarStyle ScalarStyle
        {
            get => _baseDescriptor.ScalarStyle;
            set => _baseDescriptor.ScalarStyle = value;
        }

        public bool CanWrite
            => _baseDescriptor.CanWrite;

        private readonly IPropertyDescriptor _baseDescriptor;

        public CommentsPropertyDescriptor(IPropertyDescriptor baseDescriptor)
        {
            _baseDescriptor = baseDescriptor;
            Name = baseDescriptor.Name;
        }

        public void Write(object target, object value)
            => _baseDescriptor.Write(target, value);

        public T GetCustomAttribute<T>()
            where T : Attribute
            => _baseDescriptor.GetCustomAttribute<T>();

        public IObjectDescriptor Read(object target)
        {
            var comment = _baseDescriptor.GetCustomAttribute<CommentAttribute>();
            return comment is not null
                ? new CommentsObjectDescriptor(_baseDescriptor.Read(target), comment.Comment)
                : _baseDescriptor.Read(target);
        }
    }
}