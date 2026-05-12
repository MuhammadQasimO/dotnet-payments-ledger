using System.Globalization;
using System.Net.Http;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PaymentsLedger.Domain.Common;
using PaymentsLedger.Infrastructure.Persistence;
using PaymentsLedger.Infrastructure.Webhooks;

namespace PaymentsLedger.Infrastructure.Outbox;

/// <summary>
/// Polls the outbox, dispatches unsent messages as signed HTTP webhooks, and applies an
/// exponential retry schedule. Uses <c>FOR UPDATE SKIP LOCKED</c> so multiple replicas
/// can run concurrently without double-sending.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<WebhookOptions> webhookOptions,
    IClock clock,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "Webhooks";

    private static readonly TimeSpan[] _retrySchedule =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(6),
    };

    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 50;

    private readonly WebhookOptions _opts = webhookOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDispatcher started. Polling every {Interval}.", _pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxDispatcher batch failed; will retry on next tick.");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken stoppingToken)
    {
        if (_opts.Endpoint is null)
        {
            // No webhook target configured — leave rows in the outbox; operators see them via metrics.
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

        // FOR UPDATE SKIP LOCKED: each replica claims a distinct slice, no double-send.
        var due = await db.OutboxMessages
            .FromSqlInterpolated($@"
SELECT * FROM outbox_messages
 WHERE sent_at IS NULL
   AND dead_letter = false
   AND next_attempt_at <= {clock.UtcNow}
 ORDER BY created_at ASC
 LIMIT {BatchSize}
 FOR UPDATE SKIP LOCKED")
            .ToListAsync(stoppingToken);

        if (due.Count == 0)
        {
            await tx.CommitAsync(stoppingToken);
            return;
        }

        var signer = new HmacWebhookSigner(_opts.SharedSecret);
        var client = httpClientFactory.CreateClient(HttpClientName);

        foreach (var message in due)
        {
            await DispatchSingleAsync(message, signer, client, stoppingToken);
        }

        await db.SaveChangesAsync(stoppingToken);
        await tx.CommitAsync(stoppingToken);
    }

    private async Task DispatchSingleAsync(
        OutboxMessage message,
        HmacWebhookSigner signer,
        HttpClient client,
        CancellationToken stoppingToken)
    {
        var now = clock.UtcNow;
        var signed = signer.Sign(message.PayloadJson, now);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _opts.Endpoint)
            {
                Content = new StringContent(message.PayloadJson, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Signature", signed.Signature);
            request.Headers.Add("X-Timestamp", signed.TimestampUnixSeconds);
            request.Headers.Add("X-Event-Type", message.EventType);
            request.Headers.Add(
                "X-Attempt",
                (message.Attempts + 1).ToString(CultureInfo.InvariantCulture));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(_opts.RequestTimeout);
            using var response = await client.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                message.MarkSent(now);
                logger.LogInformation(
                    "Outbox {Id} delivered ({Status}) for {EventType}",
                    message.Id, (int)response.StatusCode, message.EventType);
                return;
            }

            await HandleFailureAsync(message, $"HTTP {(int)response.StatusCode}", now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            await HandleFailureAsync(message, ex.GetType().Name + ": " + ex.Message, now);
        }
    }

    private Task HandleFailureAsync(OutboxMessage message, string error, DateTimeOffset now)
    {
        var attemptIndex = message.Attempts;
        if (attemptIndex >= _retrySchedule.Length)
        {
            message.MarkDeadLetter(error);
            logger.LogError(
                "Outbox {Id} dead-lettered after {Attempts} attempts: {Error}",
                message.Id, attemptIndex + 1, error);
        }
        else
        {
            var nextAttempt = now + _retrySchedule[attemptIndex];
            message.MarkAttempt(error, nextAttempt);
            logger.LogWarning(
                "Outbox {Id} failed (attempt {Attempt}); retry at {Next}. Error: {Error}",
                message.Id, attemptIndex + 1, nextAttempt, error);
        }
        return Task.CompletedTask;
    }
}
