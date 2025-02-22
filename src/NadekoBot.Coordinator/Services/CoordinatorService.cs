using System;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace NadekoBot.Coordinator
{
    public sealed class CoordinatorService : Coordinator.CoordinatorBase
    {
        private readonly CoordinatorRunner _runner;

        public CoordinatorService(CoordinatorRunner runner)
            => _runner = runner;

        public override Task<HeartbeatReply> Heartbeat(HeartbeatRequest request, ServerCallContext context)
        {
            var gracefulImminent = _runner.Heartbeat(request.ShardId, request.GuildCount, request.State);
            return Task.FromResult(new HeartbeatReply()
            {
                GracefulImminent =  gracefulImminent
            });
        }

        public override Task<ReshardReply> Reshard(ReshardRequest request, ServerCallContext context)
        {
            _runner.SetShardCount(request.Shards);
            return Task.FromResult(new ReshardReply());
        }

        public override Task<RestartShardReply> RestartShard(RestartShardRequest request, ServerCallContext context)
        {
            _runner.RestartShard(request.ShardId, request.Queue);
            return Task.FromResult(new RestartShardReply());
        }

        public override Task<ReloadReply> Reload(ReloadRequest request, ServerCallContext context)
        {
            _runner.ReloadConfig();
            return Task.FromResult(new ReloadReply());
        }

        public override Task<GetStatusReply> GetStatus(GetStatusRequest request, ServerCallContext context)
        {
            var status = _runner.GetShardStatus(request.ShardId);


            return Task.FromResult(StatusToStatusReply(status));
        }

        public override Task<GetAllStatusesReply> GetAllStatuses(GetAllStatusesRequest request,
            ServerCallContext context)
        {
            var statuses = _runner
                .GetAllStatuses();
            
            var reply = new GetAllStatusesReply();
            foreach (var status in statuses)
                reply.Statuses.Add(StatusToStatusReply(status));
            
            return Task.FromResult(reply);
        }

        private static GetStatusReply StatusToStatusReply(ShardStatus status)
        {
            DateTime startTime;
            try
            {
                startTime = status.Process is null or {HasExited: true}
                    ? DateTime.MinValue.ToUniversalTime()
                    : status.Process.StartTime.ToUniversalTime();
            }
            catch
            {
                startTime = DateTime.MinValue.ToUniversalTime();
            }

            var reply = new GetStatusReply()
            {
                State = status.State,
                GuildCount = status.GuildCount,
                ShardId = status.ShardId,
                LastUpdate = Timestamp.FromDateTime(status.LastUpdate),
                ScheduledForRestart = status.ShouldRestart,
                StartedAt = Timestamp.FromDateTime(startTime)
            };

            return reply;
        }

        public override Task<RestartAllReply> RestartAllShards(RestartAllRequest request, ServerCallContext context)
        {
            _runner.RestartAll(request.Nuke);
            return Task.FromResult(new RestartAllReply());
        }

        public override async Task<DieReply> Die(DieRequest request, ServerCallContext context)
        {
            if (request.Graceful)
            {
                _runner.PrepareGracefulShutdown();
                await Task.Delay(10_000);
            }

            _runner.SaveState();
            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                Environment.Exit(0);
            });

            return new DieReply();
        }

        public override Task<SetConfigTextReply> SetConfigText(SetConfigTextRequest request, ServerCallContext context)
        {
            var error = string.Empty;
            var success = true;
            try
            {
                _runner.SetConfigText(request.ConfigYml);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                success = false;
            }

            return Task.FromResult<SetConfigTextReply>(new(new()
            {
                Success = success,
                Error = error
            }));
        }

        public override Task<GetConfigTextReply> GetConfigText(GetConfigTextRequest request, ServerCallContext context)
        {
            var text = _runner.GetConfigText();
            return Task.FromResult(new GetConfigTextReply()
            {
                ConfigYml = text,
            });
        }
    }
}