﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DotNext
{
    using Runtime.CompilerServices;

    /// <summary>
    /// Provides implementation of dispose pattern.
    /// </summary>
    /// <see cref="IDisposable"/>
    /// <seealso href="https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose">Implementing Dispose method</seealso>
    [BeforeFieldInit(true)]
    public abstract class Disposable : IDisposable
    {
        private static readonly WaitCallback DisposeCallback;

        static Disposable()
        {
            DisposeCallback = UnsafeDispose;

            static void UnsafeDispose(object disposable) => Unsafe.As<IDisposable>(disposable).Dispose();
        }

        private volatile bool disposed;

        /// <summary>
        /// Indicates that this object is disposed.
        /// </summary>
        protected bool IsDisposed => disposed;

        /// <summary>
        /// Throws exception if this object is disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Object is disposed.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// Releases managed and unmanaged resources associated with this object.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> if called from <see cref="Dispose()"/>; <see langword="false"/> if called from finalizer <see cref="Finalize()"/>.</param>
        protected virtual void Dispose(bool disposing) => disposed = true;

        /// <summary>
        /// Releases all resources associated with this object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Places <see cref="IDisposable.Dispose"/> method call into thread pool.
        /// </summary>
        /// <param name="resource">The resource to be disposed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
        protected static void QueueDispose(IDisposable resource)
            => ThreadPool.QueueUserWorkItem(DisposeCallback, resource ?? throw new ArgumentNullException(nameof(resource)));

        /// <summary>
        /// Disposes many objects.
        /// </summary>
        /// <param name="objects">An array of objects to dispose.</param>
        public static void Dispose(IEnumerable<IDisposable?> objects)
        {
            foreach (var obj in objects)
                obj?.Dispose();
        }

        /// <summary>
        /// Disposes many objects.
        /// </summary>
        /// <param name="objects">An array of objects to dispose.</param>
        /// <returns>The task representing asynchronous execution of this method.</returns>
        public static async ValueTask DisposeAsync(IEnumerable<IAsyncDisposable?> objects)
        {
            foreach (var obj in objects)
            {
                if (!(obj is null))
                    await obj.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Disposes many objects in safe manner.
        /// </summary>
        /// <param name="objects">An array of objects to dispose.</param>
        public static void Dispose(params IDisposable?[] objects)
            => Dispose(objects.As<IEnumerable<IDisposable?>>());

        /// <summary>
        /// Disposes many objects in safe manner.
        /// </summary>
        /// <param name="objects">An array of objects to dispose.</param>
        /// <returns>The task representing asynchronous execution of this method.</returns>
        public static ValueTask DisposeAsync(params IAsyncDisposable?[] objects)
            => DisposeAsync(objects.As<IEnumerable<IAsyncDisposable?>>());

        /// <summary>
        /// Finalizes this object.
        /// </summary>
        ~Disposable() => Dispose(false);
    }
}
