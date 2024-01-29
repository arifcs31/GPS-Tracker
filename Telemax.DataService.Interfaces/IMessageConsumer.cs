using System.Threading;
using System.Threading.Tasks;
using Telemax.DataService.Contracts;

namespace Telemax.DataService.Interfaces;

/// <summary>
/// Represents service which consumes tracker messages from implementation-defined source.
/// </summary>
public interface IMessageConsumer
{
    /// <summary>
    /// Implements long-running message consumption logic.
    /// </summary>
    /// <param name="processMessageAsync">Message processing callback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Void task.</returns>
    Task ConsumeMessagesAsync(ProcessMessageAsync processMessageAsync, CancellationToken ct);
}


/// <summary>
/// Processes the given message.
/// </summary>
/// <param name="message">Message to process.</param>
/// <param name="force">Flag which forces requires message to be processed immediately (without bufferization).</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>Void task.</returns>
public delegate Task ProcessMessageAsync(Message? message, bool force, CancellationToken ct);


