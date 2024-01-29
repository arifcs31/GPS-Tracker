using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemax.DataService.Contracts;
using Telemax.DataService.Interfaces;

namespace Telemax.DataService.Services
{
    /// <summary>
    /// Performs message processing using <see cref="IMessageConsumer"/> and <see cref="IMessageProcessor"/>.
    /// </summary>
    public class MessageProcessingBroker : IMessageProcessingBroker
    {
        /// <summary>
        /// Service configuration.
        /// </summary>
        private readonly Configuration _config;

        /// <summary>
        /// Message consumer.
        /// </summary>
        private readonly IMessageConsumer _messageConsumer;

        /// <summary>
        /// Message processor.
        /// </summary>
        private readonly IMessageProcessor _messageProcessor;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<MessageProcessingBroker> _logger;

        /// <summary>
        /// Latest date any message was processed.
        /// </summary>
        private DateTime _lastProcessDate = DateTime.Now;


        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="options">Service options.</param>
        /// <param name="messageConsumer">Message consumer.</param>
        /// <param name="messageProcessor">Message processor.</param>
        /// <param name="logger">Logger.</param>
        public MessageProcessingBroker(
            IOptions<Configuration> options,
            IMessageConsumer messageConsumer,
            IMessageProcessor messageProcessor,
            ILogger<MessageProcessingBroker> logger)
        {
            _config = options.Value;
            _messageConsumer = messageConsumer;
            _messageProcessor = messageProcessor;
            _logger = logger;
        }


        #region IMessageProcessingBroker

        /// <inheritdoc />
        public Task ProcessAsync(CancellationToken ct) =>
            Task.Run(
                () => Task.WhenAll(
                    Task.Run(() => ConsumeAsync(ct), CancellationToken.None),
                    Task.Run(() => FlushOnIdleAsync(ct), CancellationToken.None)
                ),
                CancellationToken.None
            );

        #endregion


        /// <summary>
        /// Consumes messages using <see cref="IMessageConsumer"/>.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Void task.</returns>
        private async Task ConsumeAsync(CancellationToken ct)
        {
            try
            {
                using (var task = _messageConsumer.ConsumeMessagesAsync(ProcessMessageAsync, ct))
                    await task;
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                    _logger.LogCritical(ex, $"Unhandled {nameof(IMessageConsumer)} exception.");
            }
        }

        /// <summary>
        /// Periodically flushes messages buffered by <see cref="IMessageProcessor"/>.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Void task.</returns>
        private async Task FlushOnIdleAsync(CancellationToken ct)
        {
            var checkInterval = _config.IdleToFlush / 2;

            while (!ct.IsCancellationRequested)
            {
                if (DateTime.Now - _lastProcessDate >= _config.IdleToFlush)
                    await ProcessMessageAsync(null, true, ct);

                await Task.Delay(checkInterval, ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }

        /// <summary>
        /// Processes given message in a safe manner, optionally flushes buffered messages.
        /// </summary>
        /// <param name="message">Message to process.</param>
        /// <param name="flush">Flag which determines if buffered messages should be processed immediately.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Void task.</returns>
        private async Task ProcessMessageAsync(Message? message, bool flush, CancellationToken ct)
        {
            try
            {
                // Update last process date.
                _lastProcessDate = DateTime.Now;

                // Process given message (note that it may be just buffered for now).
                if (message != null)
                    await _messageProcessor.ProcessAsync(message, ct);

                // Process pending (buffered) messages if required (this one processes messages for sure).
                if (flush)
                    await _messageProcessor.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unhandled {nameof(IMessageProcessor)} exception.");
            }
        }


        /// <summary>
        /// Represents service configuration.
        /// </summary>
        public class Configuration
        {
            /// <summary>
            /// Gets or sets idle duration before forced flush of the buffered messages.
            /// </summary>
            public TimeSpan IdleToFlush { get; set; }
        }
    }
}
