using System.Threading;
using System.Threading.Tasks;
using Telemax.DataService.Contracts;

namespace Telemax.DataService.Interfaces;

/// <summary>
/// Represents message processor, intended to deal with deduplication, schedule message handling, etc.
/// </summary>
public interface IMessageProcessor
{
    /// <summary>
    /// Processes the given message.
    /// </summary>
    /// <param name="message">Message to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Void task.</returns>
    Task ProcessAsync(Message message, CancellationToken ct);

    /// <summary>
    /// Processes buffered messages immediately.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Void task.</returns>
    Task FlushAsync(CancellationToken ct);
}