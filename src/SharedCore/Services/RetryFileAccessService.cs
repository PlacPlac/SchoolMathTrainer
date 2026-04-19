namespace SharedCore.Services;

public sealed class RetryFileAccessService
{
    public void Execute(Action action, int retries, int delayMilliseconds)
    {
        Execute(() =>
        {
            action();
            return true;
        }, retries, delayMilliseconds);
    }

    public T Execute<T>(Func<T> action, int retries, int delayMilliseconds)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= retries; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
            }

            if (attempt < retries)
            {
                Thread.Sleep(delayMilliseconds);
            }
        }

        throw lastException ?? new IOException("Souborova operace se nezdarila.");
    }
}
