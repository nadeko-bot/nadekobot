using Grpc.Core;

namespace NadekoBot.GrpcApi;

public interface IGrpcSvc
{
    ServerServiceDefinition Bind();
}