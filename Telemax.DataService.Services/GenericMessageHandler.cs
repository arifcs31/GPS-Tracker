using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Telemax.DataService.Contracts;
using Telemax.DataService.Interfaces;

namespace Telemax.DataService.Services
{
    /// <summary>
    /// Implements <see cref="IMessageHandler"/> which delegates message handling to the specific message handlers.
    /// </summary>
    public class GenericMessageHandler : IMessageHandler
    {
        /// <summary>
        /// Message handler resolver.
        /// </summary>
        private readonly Resolver _messageHandlerResolver;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<GenericMessageHandler> _logger;


        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="messageHandlerResolver">Message handler resolver.</param>
        /// <param name="logger">Logger.</param>
        public GenericMessageHandler(Resolver messageHandlerResolver, ILogger<GenericMessageHandler> logger)
        {
            _messageHandlerResolver = messageHandlerResolver;
            _logger = logger;
        }


        #region IMessageHandler

        /// <inheritdoc />
        public async Task HandleAsync(Message message, CancellationToken ct)
        {
            try
            {
                await _messageHandlerResolver(message).HandleAsync(message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to handle message {JsonConvert.SerializeObject(message, Formatting.None)}.");
            }
        }

        #endregion


        /// <summary>
        /// Resolves message handler for the given message.
        /// </summary>
        /// <param name="message">Message to resolve message handler for.</param>
        /// <returns>Specific message handler.</returns>
        public delegate IMessageHandler Resolver(Message message);
    }
}
