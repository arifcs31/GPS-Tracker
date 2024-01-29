using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemax.DataService.Contracts;
using Telemax.DataService.Interfaces;
using Telemax.Shared.ClientDb;
using Telemax.Shared.Linq;

namespace Telemax.DataService.Services
{
    /// <summary>
    /// Implements <see cref="IMessageProcessor"/> which process messages in parallel.
    /// Under the hood it's buffers certain amount of messages and process them in parallel using multiple workers.
    /// Buffered messages processed in two cases:
    ///  - Message buffer became full after another call of <see cref="ProcessAsync"/> method;
    ///  - Message processing is requested from outside by calling <see cref="FlushAsync"/> method;
    /// </summary>
    public class MessageProcessor : IMessageProcessor
    {
        /// <summary>
        /// Service configuration.
        /// </summary>
        private readonly Configuration _config;

        /// <summary>
        /// Service scope factory.
        /// </summary>
        private readonly IServiceScopeFactory _serviceScopeFactory;

        /// <summary>
        /// Message deduplicator.
        /// </summary>
        private readonly IMessageDeduplicator _messageDeduplicator;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<MessageProcessor> _logger;


        /// <summary>
        /// Semaphore used to synchronize access to the message buffer.
        /// </summary>
        private readonly SemaphoreSlim _processingLock;

        /// <summary>
        /// Message buffer, messages stored here until partitioning and processing.
        /// </summary>
        private readonly List<Message> _messages;

        /// <summary>
        /// Map of tracker IDs to amounts of corresponding tracker messages.
        /// </summary>
        private readonly Dictionary<long, int> _messagesPerTracker;

        /// <summary>
        /// Comparer used to split list of buffered messages to relatively uniform partitions.
        /// </summary>
        private readonly IComparer<Message> _messageComparer;

        /// <summary>
        /// Underlying partitions storage.
        /// </summary>
        private readonly List<Message>[] _partitions;


        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="options">Service options.</param>
        /// <param name="serviceScopeFactory">Service scope factory.</param>
        /// <param name="messageDeduplicator">Message deduplicator.</param>
        /// <param name="logger">Logger.</param>
        public MessageProcessor(
            IOptions<Configuration> options,
            IServiceScopeFactory serviceScopeFactory,
            IMessageDeduplicator messageDeduplicator,
            ILogger<MessageProcessor> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _messageDeduplicator = messageDeduplicator;
            _logger = logger;
            _config = options.Value;

            _processingLock = new SemaphoreSlim(1, 1);
            _messages = new List<Message>();
            _messagesPerTracker = new Dictionary<long, int>();
            _messageComparer = new MessageComparer(_messagesPerTracker);

            _partitions = Enumerable.Range(0, _config.DegreeOfParallelism)
                .Select(_ => new List<Message>())
                .ToArray();
        }


        #region IMessageProcessor

        /// <inheritdoc />
        public async Task ProcessAsync(Message message, CancellationToken ct)
        {
            await _processingLock.WaitAsync(ct);
            try
            {
                // Ignore message in case it was already processed.
                if (await _messageDeduplicator.IsProcessedAsync(message, ct))
                    return;

                // Add message to the buffer.
                _messages.Add(message);
                
                // Increase amount of buffered messages for the corresponding tracker.
                _messagesPerTracker[message.TrackerId] = _messagesPerTracker.TryGetValue(message.TrackerId, out var messageCount)
                    ? messageCount + 1
                    : 1;

                // Process buffered messages in case buffer is full.
                if (_messages.Count >= _config.BufferSize)
                    await ProcessBufferedMessagesAsync(false, ct);
            }
            finally
            {
                _processingLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task FlushAsync(CancellationToken ct)
        {
            await _processingLock.WaitAsync(ct);
            try
            {
                // Process buffered messages (if any).
                if (_messages.Count > 0)
                    await ProcessBufferedMessagesAsync(true, ct);
            }
            finally
            {
                _processingLock.Release();
            }
        }

        #endregion


        /// <summary>
        /// Processes buffered messages.
        /// </summary>
        /// <param name="byDemand">Flag which determines if processing started by demand.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Void task.</returns>
        private async Task ProcessBufferedMessagesAsync(bool byDemand, CancellationToken ct)
        {
            // Log processing.
            _logger.LogInformation($"Processing {_messages.Count} messages ({(byDemand ? "by demand" : "buffer is full")}).");

            // Sort messages before splitting to the partitions.
            _messages.Sort(_messageComparer);

            // Split messages to partitions.
            for (var i = 0; i < _messages.Count;)
            {
                // Get count of the messages for the tracker of the current message.
                var count = _messagesPerTracker[_messages[i].TrackerId];

                // Get the shortest partition index.
                // TODO: Optimize!
                var shortestPartitionIdx = _partitions
                    .Select((p, idx) => ((int Index, int Size))(idx, p.Count))
                    .OrderBy(p => p.Size)
                    .Select(p => p.Index)
                    .First();

                // Add all the messages of the current message's tracker to the shortest partition.
                _partitions[shortestPartitionIdx].AddRange(_messages.Skip(i).Take(count));

                // Move to the next tracker's messages.
                i += count;
            }

            // Handle message partitions in parallel.
            await Task.WhenAll(
                _partitions
                    .Where(partition => partition.Count > 0)
                    .Select(
                        partition => Task.Run(
                            async () => await ProcessPartitionAsync(partition, ct),
                            CancellationToken.None
                        )
                    )
            );

            // Clean buffers.
            _messages.Clear();
            _messagesPerTracker.Clear();
            Array.ForEach(_partitions, p => p.Clear());
        }

        /// <summary>
        /// Processes messages partition.
        /// </summary>
        /// <param name="messages">Message partition to process.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Void task.</returns>
        private async Task ProcessPartitionAsync(IEnumerable<Message> messages, CancellationToken ct)
        {
            using (var serviceScope = _serviceScopeFactory.CreateScope())
            {
                var messageHandler = serviceScope.ServiceProvider.GetRequiredService<IMessageHandler>();

                var dbContext = serviceScope.ServiceProvider.GetRequiredService<ClientDbContext>();
                dbContext.Database.AutoTransactionsEnabled = false;
                
                foreach (var chunk in messages.Split(42))
                {
                    foreach (var message in chunk)
                    {
                        if (ct.IsCancellationRequested)
                            break;

                        try
                        {
                            await messageHandler.HandleAsync(message, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            return; // TODO: Refine cancellation?
                        }
                        catch (Exception ex)
                        {
                            if (ex is not OperationCanceledException)
                                _logger.LogError(ex, $"Unhandled {nameof(IMessageHandler)} exception.");
                        }
                    }

                    await dbContext.SaveChangesAsync(ct);
                    dbContext.ChangeTracker.Clear();
                }
            }
        }


        private class MessagePartitioner : IEnumerable<MessageListSegment>
        {
            public IEnumerator<MessageListSegment> GetEnumerator()
            {
                throw new NotImplementedException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private readonly struct MessageListSegment
        {
            
        }


        /// <summary>
        /// Comparer used to sort messages before splitting them to the partitions:
        /// Sorting should be done according to the next requirements:
        ///  - All the messages for the specific tracker should be owned by one and only one partition;
        ///  - Messages of the specific tracker should come together (in one line);
        ///  - Older messages should come first.
        /// </summary>
        private class MessageComparer : IComparer<Message>
        {
            /// <summary>
            /// Map of tracker IDs to amounts of corresponding tracker messages.
            /// </summary>
            private readonly IReadOnlyDictionary<long, int> _messagesPerTracker;


            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="messagesPerTracker">Map of tracker IDs to amounts of corresponding tracker messages.</param>
            public MessageComparer(IReadOnlyDictionary<long, int> messagesPerTracker) =>
                _messagesPerTracker = messagesPerTracker;


            /// <inheritdoc />
            public int Compare(Message? a, Message? b)
            {
                var aWeight = _messagesPerTracker[a!.TrackerId];
                var bWeight = _messagesPerTracker[b!.TrackerId];

                // Sort by amount messages per tracker.
                // Messages for the trackers with the bigger amount of messages should be placed first.
                if (aWeight < bWeight)
                    return 1;
                if (aWeight > bWeight)
                    return -1;

                // Then sort by tracker ID, so the messages for the specific tracker come together.
                if (a.TrackerId < b.TrackerId)
                    return -1;
                if (a.TrackerId > b.TrackerId)
                    return 1;

                // Then sort by receive date, old messages come first.
                if (a.ReceiveDate < b.ReceiveDate)
                    return -1;
                if (a.ReceiveDate > b.ReceiveDate)
                    return 1;

                return 0;
            }
        }


        /// <summary>
        /// Represents service configuration.
        /// </summary>
        public class Configuration
        {
            /// <summary>
            /// Gets or sets buffer size.
            /// </summary>
            public int BufferSize { get; set; }
            
            /// <summary>
            /// Gets or sets the amount of threads used to process buffered messages.
            /// </summary>
            public int DegreeOfParallelism { get; set; }
        }
    }
}
