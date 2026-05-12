namespace PaymentsLedger.Infrastructure.Webhooks;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>Target HTTP endpoint for outbox-dispatched events.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>HMAC shared secret. Set per-environment via secret store, never in source.</summary>
    public string SharedSecret { get; init; } = "change-me";

    /// <summary>Per-attempt HTTP timeout.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
