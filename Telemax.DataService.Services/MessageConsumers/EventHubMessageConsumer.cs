using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Telemax.DataService.Contracts;
using Telemax.DataService.Interfaces;

namespace Telemax.DataService.Services
{
    /// <summary>
    /// Implements <see cref="IMessageConsumer"/> by consuming messages from the Azure Event Hub.
    /// </summary>
    public class EventHubMessageConsumer : IMessageConsumer
    {
        /// <summary>
        /// Service config.
        /// </summary>
        private readonly Configuration _config;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<EventHubMessageConsumer> _logger;

        /// <summary>
        /// Checkpoint policy.
        /// </summary>
        private readonly CheckpointPolicy _checkpointPolicy;

        /// <summary>
        /// Message processing callback (set by <see cref="ConsumeMessagesAsync"/>).
        /// </summary>
        private ProcessMessageAsync _processMessageAsync = null!;


        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="logger">Logger.</param>
        public EventHubMessageConsumer(IOptions<Configuration> options, ILogger<EventHubMessageConsumer> logger)
        {
            _config = options.Value;
            _logger = logger;
            _checkpointPolicy = new CheckpointPolicy(_config.CheckpointSize, _config.CheckpointInterval);
        }


        #region IMessageConsumer
        
        /// <inheritdoc />
        public async Task ConsumeMessagesAsync(ProcessMessageAsync processMessageAsync, CancellationToken ct)
        {
            // Set state used by the event handlers.
            _checkpointPolicy.Reset();
            _processMessageAsync = processMessageAsync;

            // Create event processor client.
            var client = new EventProcessorClient(
                new BlobContainerClient(_config.BlobStorageConnStr, _config.BlobContainerName),
                _config.EventHubConsumerGroup,
                _config.EventHubConnStr,
                _config.EventHubName
            );
            
            // Subscribe for client events.
            client.ProcessEventAsync += ClientOnProcessEventAsync;
            client.PartitionInitializingAsync += ClientOnPartitionInitializingAsync;
            client.PartitionClosingAsync += ClientOnPartitionClosingAsync;
            client.ProcessErrorAsync += ClientOnProcessErrorAsync;

            // Start event processing.
            await client.StartProcessingAsync(ct);

            // Wait for the cancellation.
            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.SetResult());
            await tcs.Task;

            // Stop event processing.
            using (var stopCts = new CancellationTokenSource(_config.StopTimeout))
                await client.StopProcessingAsync(stopCts.Token);
        }

        #endregion


        /// <summary>
        /// Handles message event.
        /// </summary>
        /// <param name="arg">Event arguments.</param>
        /// <returns>Void task.</returns>
        private async Task ClientOnProcessEventAsync(ProcessEventArgs arg)
        {
            try
            {
                // Skip in case no event present.
                if (!arg.HasEvent)
                    return;
                
                // Check if checkpoint update is required.
                var isCheckpointUpdateRequired = _checkpointPolicy.Increment();

                // Try to deserialize and process message.
                if (TryDeserializeMessage(arg, out var message))
                    await _processMessageAsync(message, isCheckpointUpdateRequired, arg.CancellationToken);

                // Update checkpoint if required.
                if (isCheckpointUpdateRequired)
                    await CheckpointAsync(arg);
            }
            catch (OperationCanceledException)
            {
                // Ignore message processing cancellation, note that checkpoint update is not required in this case.
            }
        }

        /// <summary>
        /// Handles partition initialization event.
        /// </summary>
        /// <param name="arg">Event arguments.</param>
        /// <returns>Void task.</returns>
        private Task ClientOnPartitionInitializingAsync(PartitionInitializingEventArgs arg)
        {
            _logger.LogInformation($"Partition {arg.PartitionId} is initializing.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles partition closing event.
        /// </summary>
        /// <param name="arg">Event arguments.</param>
        /// <returns>Void task.</returns>
        private Task ClientOnPartitionClosingAsync(PartitionClosingEventArgs arg)
        {
            _logger.LogInformation($"Partition {arg.PartitionId} is closing: {arg.Reason}.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles error event.
        /// </summary>
        /// <param name="arg">Event arguments.</param>
        /// <returns>Void task.</returns>
        private Task ClientOnProcessErrorAsync(ProcessErrorEventArgs arg)
        {
            _logger.LogError(arg.Exception, $"Error occurred within the {nameof(EventProcessorClient)}.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Tries to retrieve tracker message from the given process event arguments.
        /// </summary>
        /// <param name="arg">Process event arguments.</param>
        /// <param name="message">Retrieved message.</param>
        /// <returns>True if message was successfully retrieved, false otherwise.</returns>
        private bool TryDeserializeMessage(ProcessEventArgs arg, out Message? message)
        {
            message = null;

            // Retrieve message type.
            Type messageType;
            try
            {
                if (!arg.Data.Properties.TryGetValue(MessageTypeHelper.MessageTypeProp, out var typeCode))
                    throw new InvalidOperationException($"Message type-code property \"{MessageTypeHelper.MessageTypeProp}\" is not set.");
                messageType = MessageTypeHelper.GetType((MessageType)typeCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to determine message type.");
                return false;
            }

            // Retrieve message JSON.
            string messageJson;
            try
            {
                messageJson = arg.Data.EventBody.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to convert message data to string.");
                return false;
            }

            // Retrieve message.
            try
            {
                message = JsonConvert.DeserializeObject(messageJson, messageType) as Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to deserialize message from JSON \"{messageJson}\".");
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private async Task CheckpointAsync(ProcessEventArgs arg)
        {
            try
            {
                await arg.UpdateCheckpointAsync(arg.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to update checkpoint.");
            }
        }


        /// <summary>
        /// Represents service configuration.
        /// </summary>
        public class Configuration
        {
            /// <summary>
            /// Gets or sets the event hubs namespace connection string.
            /// </summary>
            public string EventHubConnStr { get; set; } = null!;

            /// <summary>
            /// Gets or sets event hub name.
            /// </summary>
            public string EventHubName { get; set; } = null!;

            /// <summary>
            /// Gets or sets event hub consumer group.
            /// </summary>
            public string EventHubConsumerGroup { get; set; } = null!;

            /// <summary>
            /// Gets or sets blob storage connection string.
            /// </summary>
            public string BlobStorageConnStr { get; set; } = null!;

            /// <summary>
            /// Gets or sets the name of the blob container used to store checkpoints.
            /// </summary>
            public string BlobContainerName { get; set; } = null!;

            /// <summary>
            /// Gets or sets the maximal amount of messages between checkpoints.
            /// </summary>
            public int CheckpointSize { get; set; }

            /// <summary>
            /// Gets or sets the maximal amount of time between checkpoints.
            /// </summary>
            public TimeSpan CheckpointInterval { get; set; }

            /// <summary>
            /// Gets or sets service stop timeout.
            /// </summary>
            public TimeSpan StopTimeout { get; set; }
        }
    }
}
