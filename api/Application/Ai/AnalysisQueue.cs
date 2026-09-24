using System.Threading.Channels;

namespace Finance.Api.Application.Ai;

/// <summary>One analysis to run, and the user whose scope runs it.</summary>
public sealed record AnalysisWorkItem(Guid UserId, Guid AnalysisId);

/// <summary>
/// The analysis job's wake-up channel (ADR-003, final form): in-process and unbounded. The
/// <c>AiAnalyses</c> row is the state, so an item lost with the process is found again by the
/// job's sweep, and an item enqueued twice runs once.
/// </summary>
public sealed class AnalysisQueue
{
    private readonly Channel<AnalysisWorkItem> channel =
        Channel.CreateUnbounded<AnalysisWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<AnalysisWorkItem> Reader => channel.Reader;

    /// <remarks>Never blocks and never fails: the channel is unbounded and never completed.</remarks>
    public void Enqueue(Guid userId, Guid analysisId) => channel.Writer.TryWrite(new AnalysisWorkItem(userId, analysisId));
}
