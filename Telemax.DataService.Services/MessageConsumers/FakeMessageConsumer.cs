using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Telemax.DataService.Contracts;
using Telemax.DataService.Interfaces;


namespace Telemax.DataService.Services
{
    /// <summary>
    /// Implements <see cref="IMessageConsumer"/> by generating messages using given factory.
    /// </summary>
    public class FakeMessageConsumer : IMessageConsumer
    {
        /// <summary>
        /// Service config.
        /// </summary>
        private readonly Configuration _config;

        /// <summary>
        /// Message factory function.
        /// </summary>
        private readonly Func<long, Message> _messageFactory;


        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="messageFactory">Message factory function, accepts message index.</param>
        public FakeMessageConsumer(IOptions<Configuration> options, Func<long, Message> messageFactory)
        {
            _messageFactory = messageFactory;
            _config = options.Value;
        }


        /// <inheritdoc />
        public async Task ConsumeMessagesAsync(ProcessMessageAsync processMessageAsync, CancellationToken ct)
        {
            // Define minimal async delay (ticks).
            var minAsyncDelay = TimeSpan.FromMilliseconds(100).Ticks;

            // Define message index, create checkpoint policy and minimal async delay (ticks).
            var messageIndex = 0L;
            var checkpointPolicy = new CheckpointPolicy(_config.CheckpointSize, _config.CheckpointInterval);
            
            // Define start date and message population interval (ticks).
            var startDate = Stopwatch.GetTimestamp();
            var messageInterval = _config.MessagePopulationInterval.Ticks;

            // Populate messages until cancellation:
            while (!ct.IsCancellationRequested)
            {
                // Calculate next message date and delay before it (ticks).
                var messageStart = startDate + messageInterval * messageIndex;
                var messageDelay = messageStart - Stopwatch.GetTimestamp();
                
                // Wait before the next message if required.
                if (messageDelay > 0)
                {
                    if (messageDelay >= minAsyncDelay)
                        await Task.Delay(new TimeSpan(messageDelay), ct).ContinueWith(_ => { }, CancellationToken.None);
                    else
                        // ReSharper disable once EmptyEmbeddedStatement
                        while (!ct.IsCancellationRequested && messageStart > Stopwatch.GetTimestamp());
                }

                // Check if checkpoint should be updated.
                var checkpoint = checkpointPolicy.Increment();

                // Deserialize and process message.
                var message = _messageFactory(messageIndex++);
                await processMessageAsync(message, checkpoint, ct);
            }
        }


        /// <summary>
        /// Represents service configuration.
        /// </summary>
        public class Configuration
        {
            /// <summary>
            /// Gets or sets message population interval.
            /// </summary>
            public TimeSpan MessagePopulationInterval { get; set; }

            /// <summary>
            /// Gets or sets the maximal amount of messages between checkpoints.
            /// </summary>
            public int CheckpointSize { get; set; }

            /// <summary>
            /// Gets or sets the maximal amount of time between checkpoints.
            /// </summary>
            public TimeSpan CheckpointInterval { get; set; }
        }
    }
}