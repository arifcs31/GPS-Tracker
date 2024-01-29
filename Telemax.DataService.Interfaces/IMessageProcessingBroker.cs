using System.Threading;
using System.Threading.Tasks;

namespace Telemax.DataService.Interfaces;

/// <summary>
/// Performs message processing which involves communication of several separate services.
/// </summary>
public interface IMessageProcessingBroker
{
    /// <summary>
    /// Processes messages.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Void task.</returns>
    public Task ProcessAsync(CancellationToken ct);
}