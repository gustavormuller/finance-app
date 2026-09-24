using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Application.Ai;

/// <summary>
/// Every AI call goes through here: the <c>ai_enabled</c> gate (ADR-010), the budget
/// (ADR-008), the call, and the usage row, written whether the call succeeded or not
/// (009, decision 3). Each call is timed out on <see cref="TimeProvider"/> after its
/// purpose's <c>TimeoutSeconds</c>, whichever adapter answers. The user is the scope's: the signed-in caller, or the user a job's
/// scope acts for (<see cref="ActingUser"/>), so the query filters stay on.
/// </summary>
/// <remarks>
/// The usage row is saved on the scope's context. A caller must not hold unsaved changes it
/// would not want saved with it.
/// </remarks>
public sealed class AiGateway(
    AppDbContext db,
    ICurrentUser currentUser,
    IAiProvider provider,
    BudgetGuard budget,
    AiPricing pricing,
    IOptions<AiOptions> options,
    TimeProvider clock)
{
    /// <summary>The fake's name in <c>AiUsage.Provider</c>, so its rows never pass for a real provider's.</summary>
    public const string FakeProviderName = "fake";

    /// <exception cref="AiDisabledException">The user has not turned AI on.</exception>
    /// <exception cref="AiBudgetExceededException">The user's month has reached the budget.</exception>
    /// <exception cref="AiProviderTimeoutException">The call ran past the purpose's <c>TimeoutSeconds</c>; recorded.</exception>
    /// <exception cref="AiProviderException">The provider failed; recorded with what it reported.</exception>
    public async Task<AiCompletion> CompleteAsync(AiPurpose purpose, string system, string user, int maxTokens, CancellationToken ct)
    {
        var userId = currentUser.Id ?? throw new InvalidOperationException("An AI call needs a user: a request's or a job scope's.");
        if (!await db.Users.Where(appUser => appUser.Id == userId).Select(appUser => appUser.AiEnabled).SingleOrDefaultAsync(ct))
        {
            throw new AiDisabledException();
        }

        var month = AiCost.MonthOf(clock.GetUtcNow());
        await budget.EnsureWithinBudgetAsync(userId, month, ct);

        var task = TaskFor(purpose);
        var request = new AiRequest(task.Model, system, user, maxTokens);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(task.TimeoutSeconds), clock);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        AiCompletion completion;
        try
        {
            completion = await provider.CompleteAsync(request, call.Token);
        }
        catch (OperationCanceledException cancelled) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await RecordAsync(userId, month, purpose, request.Model, EstimatedInputTokens(request), 0, succeeded: false);
            throw new AiProviderTimeoutException(
                $"The {purpose} call to '{request.Model}' ran past {task.TimeoutSeconds} s.", cancelled);
        }
        catch (Exception failure)
        {
            var (input, output) = failure is AiProviderException { InputTokens: { } reported } known
                ? (reported, known.OutputTokens)
                : (EstimatedInputTokens(request), 0);
            await RecordAsync(userId, month, purpose, request.Model, input, output, succeeded: false);
            throw;
        }

        await RecordAsync(userId, month, purpose, request.Model, completion.InputTokens, completion.OutputTokens, succeeded: true);
        return completion;
    }

    /// <summary>
    /// A failure that reports no usage, such as a timeout, most likely still read the input.
    /// Four characters a token, rounded up: rough, and it errs towards the budget refusing.
    /// </summary>
    private static int EstimatedInputTokens(AiRequest request) =>
        (int)Math.Ceiling((request.System.Length + (long)request.User.Length) / 4m);

    private AiTaskOptions TaskFor(AiPurpose purpose) => purpose switch
    {
        AiPurpose.Categorisation => options.Value.Categorisation,
        AiPurpose.Analysis => options.Value.Analysis,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown AI purpose."),
    };

    // Not cancellable: the tokens were spent whether or not the caller is still waiting.
    private async Task RecordAsync(Guid userId, string month, AiPurpose purpose, string model, int input, int output, bool succeeded)
    {
        db.AiUsage.Add(new AiUsage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Month = month,
            Purpose = purpose,
            Provider = options.Value.FakeProvider ? FakeProviderName : options.Value.Provider.ToLowerInvariant(),
            Model = model,
            InputTokens = input,
            OutputTokens = output,
            CostBrl = await pricing.CostBrlAsync(model, input, output, CancellationToken.None),
            Succeeded = succeeded,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
