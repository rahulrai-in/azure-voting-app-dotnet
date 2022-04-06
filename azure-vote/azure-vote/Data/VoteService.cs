using System.Diagnostics;
using Microsoft.Extensions.Options;
using Prometheus;
using StackExchange.Redis;

namespace AzureVote.Data;

public class VoteService
{
    private const string Vote1Key = "VOTE1";
    private const string Vote2Key = "VOTE2";
    private readonly ActivitySource _activitySource;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly Counter _resetCounter;
    private readonly VoteAppSettings _settings;
    private readonly Counter _votesCounter;

    public VoteService(IConnectionMultiplexer multiplexer, IOptions<VoteAppSettings> settings,
        ActivitySource activitySource)
    {
        _multiplexer = multiplexer;
        _activitySource = activitySource;
        _settings = settings.Value;
        _votesCounter = Metrics.CreateCounter("vote_count_total", "Counts number of votes cast");
        _resetCounter = Metrics.CreateCounter("reset_count_total", "Counts number of resets");
    }

    public async Task<(Vote vote1, Vote vote2)> GetVotesAsync()
    {
        using var activity = _activitySource.StartActivity(nameof(GetVotesAsync), ActivityKind.Server);
        var redis = _multiplexer.GetDatabase();
        return await GetVotes(redis);
    }

    public async Task<(Vote vote1, Vote vote2)> IncrementVoteAsync(int? candidate)
    {
        using var activity = _activitySource.StartActivity(nameof(IncrementVoteAsync), ActivityKind.Server);
        activity?.AddEvent(new ActivityEvent("Vote added"));
        activity?.SetTag(nameof(candidate), candidate);
        var redis = _multiplexer.GetDatabase();
        switch (candidate)
        {
            case 1:
                await redis.StringIncrementAsync(Vote1Key);
                _votesCounter.Inc();
                break;
            case 2:
                await redis.StringIncrementAsync(Vote2Key);
                _votesCounter.Inc();
                break;
        }

        return await GetVotes(redis);
    }

    public async Task<(Vote vote1, Vote vote2)> ResetVotesAsync()
    {
        using var activity = _activitySource.StartActivity(nameof(ResetVotesAsync), ActivityKind.Server);
        activity?.AddEvent(new ActivityEvent("Reset event"));
        var redis = _multiplexer.GetDatabase();
        await redis.StringSetAsync(Vote1Key, 0);
        await redis.StringSetAsync(Vote2Key, 0);
        _resetCounter.Inc();
        return await GetVotes(redis);
    }

    public string GetTitle()
    {
        return _settings.Title;
    }

    private async Task<(Vote vote1, Vote vote2)> GetVotes(IDatabase redis)
    {
        var vote1Count = await redis.StringGetAsync(Vote1Key);
        var vote2Count = await redis.StringGetAsync(Vote2Key);
        return (new Vote(_settings.Vote1Label, vote1Count.TryParse(out long val1) ? val1 : 0),
            new Vote(_settings.Vote2Label, vote2Count.TryParse(out long val2) ? val2 : 0));
    }
}
