using Microsoft.AspNetCore.Mvc;

using PaymentsLedger.Api.Contracts;
using PaymentsLedger.Application.Wallets;

namespace PaymentsLedger.Api.Controllers;

[ApiController]
[Route("api/wallets")]
public sealed class WalletsController(
    CreateWalletHandler createHandler,
    GetBalanceHandler balanceHandler) : ControllerBase
{
    /// <summary>Create a new wallet for a user in a single currency.</summary>
    [HttpPost]
    [ProducesResponseType<CreateWalletResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateWalletResponse>> Create(
        [FromBody] CreateWalletRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await createHandler.HandleAsync(
            new CreateWalletCommand(request.UserId, request.Currency.ToUpperInvariant()),
            cancellationToken);

        var response = new CreateWalletResponse(
            result.WalletId,
            result.Currency,
            MoneyDto.From(result.Balance));

        return CreatedAtAction(nameof(GetBalance), new { id = result.WalletId }, response);
    }

    /// <summary>
    /// Get a wallet's current balance, or its historical balance as of <paramref name="asOf"/>.
    /// </summary>
    [HttpGet("{id:guid}/balance")]
    [ProducesResponseType<BalanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BalanceResponse>> GetBalance(
        Guid id,
        [FromQuery] DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        var result = await balanceHandler.HandleAsync(
            new GetBalanceQuery(id, asOf),
            cancellationToken);

        return Ok(new BalanceResponse(result.WalletId, MoneyDto.From(result.Balance), asOf));
    }
}
