#region Using directives

using System;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.Utils
{
    public static class SemaphoreSlimExtension
    {
        public static async Task<IDisposable> UseWaitAsync(
            this SemaphoreSlim semaphore,
#pragma warning disable IDE0034 // Simplifier l'expression 'default'
            CancellationToken cancelToken = default(CancellationToken))
#pragma warning restore IDE0034 // Simplifier l'expression 'default'
        {
            await semaphore.WaitAsync(100, cancelToken).ConfigureAwait(false);
            return new ReleaseWrapper(semaphore);
        }

        private class ReleaseWrapper : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;

            private bool _isDisposed;

            public ReleaseWrapper(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                if (_isDisposed)
                    return;

                _semaphore.Release();
                _isDisposed = true;
            }
        }
    }
}