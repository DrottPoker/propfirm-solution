using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>Work for the trading platform, queued in the same transaction as the decision that caused it.</summary>
internal abstract record TradingCommand;

/// <summary>Opens the account for the trader, creating the trader's user on the trading platform first if needed.</summary>
internal sealed record OpenTradingAccount(string AccountId, decimal InitialBalance, Guid TraderId) : TradingCommand;

internal sealed record SetTradingFloor(string AccountId, string FloorId, FloorSpec Floor) : TradingCommand;

internal sealed record CloseTradingAccount(string AccountId) : TradingCommand;

/// <summary>Stops new positions on the account while the challenge is paused.</summary>
internal sealed record SuspendTradingAccount(string AccountId) : TradingCommand;

internal sealed record ResumeTradingAccount(string AccountId) : TradingCommand;

/// <summary>
/// Withdraws <paramref name="Amount"/> (a positive number) once, keeping at least <paramref name="MinBalance"/>.
/// A refusal fails the payout <paramref name="OperationId"/>.
/// </summary>
internal sealed record WithdrawFromTradingAccount(string AccountId, string OperationId, decimal Amount, decimal MinBalance) : TradingCommand;
