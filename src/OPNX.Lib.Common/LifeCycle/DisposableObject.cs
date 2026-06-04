namespace OPNX.Lib.Common.LifeCycle
{
    public abstract class DisposableObject : IDisposable, IAsyncDisposable
    {
        #region Fields
        private int _disposed;
        #endregion

        #region Properties
        protected bool IsDisposed => Volatile.Read(ref _disposed) == 1;
        #endregion

        #region Public Methods
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            OnDispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await OnDisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private / Protected Methods
        protected virtual void OnDispose()
        {
        }


        protected virtual ValueTask OnDisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
        #endregion
    }
}

