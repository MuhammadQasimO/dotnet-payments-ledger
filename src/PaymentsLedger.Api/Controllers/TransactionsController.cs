using Microsoft.AspNetCore.Mvc;

using PaymentsLedger.Api.Contracts;
using PaymentsLedger.Application.Transactions;
using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Api.Controllers;

[ApiController]
[Route("api/transactions")]
public sealed class TransactionsController(GetTransactionHandler handler) : ControllerBase
{
    /// <summary>Fetch a transaction with all of its ledger entries.</summary>
    [HttpGet("{id:guid}", Name = "GetTransaction")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetTransactionQuery(id), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        var response = new TransactionResponse(
            result.TransactionId,
            result.IdempotencyKey,
            result.CreatedAt,
            result.Status,
            result.Entries
                .Select(e => new TransferEntryDto(
                    e.LedgerEntryId,
                    e.WalletId,
                    MoneyDto.From(new Money(e.AmountMinorUnits, e.Currency))))
                .ToList());

        return Ok(response);
    }
}
