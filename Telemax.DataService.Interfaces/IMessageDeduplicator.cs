using System.Threading;
using System.Threading.Tasks;
using Telemax.DataService.Contracts;

namespace Telemax.DataService.Interfaces;

/// <summary>
/// Represents service which allows to check if the specific tracker message was processed already.
/// </summary>
public interface IMessageDeduplicator
{
    /// <summary>
    /// Check if the given message was processed.
    /// </summary>
    /// <param name="message">Message to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Task which resolves with true in case given message was processed.</returns>
    Task<bool> IsProcessedAsync(Message message, CancellationToken ct);

    /// <summary>
    /// Marks message as processed.
    /// </summary>
    /// <param name="message">Message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Void task.</returns>
    Task MarkAsProcessedAsync(Message message, CancellationToken ct);
}