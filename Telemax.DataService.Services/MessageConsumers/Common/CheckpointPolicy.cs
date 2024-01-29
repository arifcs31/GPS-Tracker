using System;

namespace Telemax.DataService.Services
{
    /// <summary>
    /// Represents checkpoint policy.
    /// </summary>
    internal class CheckpointPolicy
    {
        /// <summary>
        /// Maximal amount of "unchecked" messages.
        /// </summary>
        private readonly int _checkpointSize;
        
        /// <summary>
        /// Time interval between checkpoints.
        /// </summary>
        private readonly TimeSpan _checkpointInterval;


        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="checkpointSize">Maximal amount of "unchecked" messages.</param>
        /// <param name="checkpointInterval">Time interval between checkpoints.</param>
        public CheckpointPolicy(int checkpointSize, TimeSpan checkpointInterval)
        {
            _checkpointSize = checkpointSize;
            _checkpointInterval = checkpointInterval;
        }


        /// <summary>
        /// Gets current amount of unchecked messages.
        /// </summary>
        public int CheckpointSize { get; private set; }

        /// <summary>
        /// Gets last checkpoint date.
        /// </summary>
        public DateTime LastCheckpointDate { get; private set; } = DateTime.Now;


        /// <summary>
        /// Increments amount of "unchecked" messages.
        /// Resets amount of "unchecked" messages and last checkpoint date in case next checkpoint is reached.
        /// </summary>
        /// <returns>True in case checkpoint update should be done, false otherwise.</returns>
        public bool Increment()
        {
            CheckpointSize++;

            if (CheckpointSize < _checkpointSize && DateTime.Now < LastCheckpointDate + _checkpointInterval)
                return false;

            CheckpointSize = 0;
            LastCheckpointDate = DateTime.Now;
            return true;
        }

        /// <summary>
        /// Resets policy state.
        /// </summary>
        public void Reset()
        {
            CheckpointSize = 0;
            LastCheckpointDate = DateTime.Now;
        }
    }
}
