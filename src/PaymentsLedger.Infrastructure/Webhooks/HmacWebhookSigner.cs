using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PaymentsLedger.Infrastructure.Webhooks;

/// <summary>
/// HMAC-SHA256 signature over <c>{unix-timestamp}.{body}</c>. Receivers verify by
/// reconstructing the same string with the <c>X-Timestamp</c> header and comparing
/// against <c>X-Signature</c> (prefixed <c>sha256=</c> for forward-compat with v2 schemes).
/// </summary>
public sealed class HmacWebhookSigner(string secret)
{
    private readonly byte[] _secret = Encoding.UTF8.GetBytes(secret ?? throw new ArgumentNullException(nameof(secret)));

    public SignedPayload Sign(string body, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(body);

        var unix = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var toSign = unix + "." + body;
        var hash = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(toSign));
        return new SignedPayload("sha256=" + Convert.ToHexString(hash).ToLowerInvariant(), unix);
    }
}

public sealed record SignedPayload(string Signature, string TimestampUnixSeconds);
