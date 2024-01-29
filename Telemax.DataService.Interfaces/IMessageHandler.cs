using System.Threading;
using System.Threading.Tasks;
using Telemax.DataService.Contracts;

namespace Telemax.DataService.Interfaces;

/// <summary>
/// Represents service which actually handles tracker messages: updates client data, fires various alerts, etc...
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// Handles given message.
    /// </summary>
    /// <param name="message">Message to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Void task.</returns>
    Task HandleAsync(Message message, CancellationToken ct);
}