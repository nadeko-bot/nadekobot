#nullable enable
using System;
using System.Text.Json.Serialization;

namespace NadekoBot.Coordinator.Api;

public sealed record CoordHealthDto(string Status, int TotalShards, int ConnectedShards);

public sealed record ShardDto(
    int ShardId,
    string State,
    int GuildCount,
    double UptimePercent,
    DateTime? StartedAt,
    bool ScheduledForRestart,
    DateTime LastUpdate);

public sealed record CoordStatusDto(int TotalShards, int ConnectedShards, int GuildCount, ShardDto[] Shards);

public sealed record GuildShardDto(int ShardId, string State);

public sealed record CoordErrorDto(string Error);

public sealed record HeartbeatRequest(string? Token, int GuildCount, string State);

public sealed record RestartAllRequest(bool Nuke);

public sealed record DieRequest(bool Graceful);

public sealed record VoteRequest(string? UserId, string? Type);

public sealed record TakeVotesRequest(string? Token);

public sealed record VoteDto(string UserId, string Type);

public sealed record VotesDto(VoteDto[] Votes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CoordHealthDto))]
[JsonSerializable(typeof(CoordStatusDto))]
[JsonSerializable(typeof(GuildShardDto))]
[JsonSerializable(typeof(CoordErrorDto))]
[JsonSerializable(typeof(HeartbeatRequest))]
[JsonSerializable(typeof(RestartAllRequest))]
[JsonSerializable(typeof(DieRequest))]
[JsonSerializable(typeof(VoteRequest))]
[JsonSerializable(typeof(TakeVotesRequest))]
[JsonSerializable(typeof(VotesDto))]
public sealed partial class CoordJsonContext : JsonSerializerContext
{
}
