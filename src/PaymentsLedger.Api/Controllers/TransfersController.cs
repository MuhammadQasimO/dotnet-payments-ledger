using Microsoft.AspNetCore.Mvc;

using PaymentsLedger.Api.Contracts;
using PaymentsLedger.Api.Middleware;
using PaymentsLedger.Application.Transfers;
using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Api.Controllers;

[ApiController]
[Route("api/transfers")]
public sealed class TransfersController(TransferHandler handler) : ControllerBase
{
    /// <summary>
    /// Post a same-currency transfer. Requires the <c>Idempotency-Key</c> header;
    /// repeating the same key with the same body replays the original response, and
    /// reusing the key with a different body returns 409.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TransferResponse>> Post(
        [FromBody] TransferRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The Idempotency middleware guarantees the header is present and stashes it in
        // HttpContext.Items. Pulling it from there keeps the controller signature clean.
        var idempotencyKey = HttpContext.Items[IdempotencyMiddleware.IdempotencyKeyItem] as string
            ?? throw new InvalidOperationException(
                "Idempotency-Key is missing — IdempotencyMiddleware should have rejected this request.");

        var result = await handler.HandleAsync(
            new TransferCommand(
                idempotencyKey,
                request.FromWalletId,
                request.ToWalletId,
                request.AmountMinorUnits,
                request.Currency.ToUpperInvariant(),
                request.Reference),
            cancellationToken);

        var response = new TransferResponse(
            result.TransactionId,
            result.Status,
            result.Entries
                .Select(e => new TransferEntryDto(
                    e.LedgerEntryId,
                    e.WalletId,
                    MoneyDto.From(new Money(e.AmountMinorUnits, e.Currency))))
                .ToList());

        return CreatedAtAction(
            actionName: nameof(TransactionsController.Get),
            controllerName: "Transactions",
            routeValues: new { id = result.TransactionId },
            value: response);
    }
}
