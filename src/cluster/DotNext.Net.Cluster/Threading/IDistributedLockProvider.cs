using System;

namespace DotNext.Threading
{
    /// <summary>
    /// Represents distributed exclusive lock manager.
    /// </summary>
    public interface IDistributedLockProvider 
    {

        /// <summary>
        /// Gets the distributed lock.
        /// </summary>
        /// <param name="lockName">The name of distributed lock.</param>
        /// <returns>The distributed lock.</returns>
        /// <exception cref="ArgumentException"><paramref name="lockName"/> is empty string; or contains invalid characters</exception>
        AsyncLock this[string lockName] { get; }

        /// <summary>
        /// Sets configuration provider for distributed locks.
        /// </summary>
        /// <value>The configuration provider.</value>
        DistributedLockConfigurationProvider Configuration { set; }

        /// <summary>
        /// Releases the lock in unsafe manner.
        /// </summary>
        /// <remarks>
        /// This method should be used for maintenance purposes only if the particular lock is acquired for a long period of time
        /// and its owner crashed. In normal situation, this method can cause acquisition of the same lock by multiple requesters in a splitted cluster.
        /// </remarks>
        /// <param name="lockName">The name of distributed lock.</param>
        /// <returns>The task represention unlock async operation.</returns>
        /// <exception cref="ArgumentException"><paramref name="lockName"/> is empty string; or contains invalid characters.</exception>
        void ForceUnlock(string lockName);
    }
}