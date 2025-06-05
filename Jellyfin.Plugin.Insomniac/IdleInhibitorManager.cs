using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Insomniac.Inhibitors;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Insomniac;

/// <summary>
/// Idle inhibitor.
/// </summary>
public sealed class IdleInhibitorManager
{
    private readonly ILogger<IdleInhibitorManager> _logger;

    private readonly IInhibitor _inhibitor = new DbusLoginManagerInhibitor();

    /// <summary>
    /// Initializes a new instance of the <see cref="IdleInhibitorManager"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger.</param>
    public IdleInhibitorManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<IdleInhibitorManager>();
    }

    public IdleInhibitor CreateInhibitor(string reason)
    {
        return new IdleInhibitor(_inhibitor, reason);
    }

    public sealed class IdleInhibitor : IAsyncDisposable
    {
        private readonly IInhibitor _inhibitor;

        private readonly string _reason;

        private Func<Task>? _releaseFunc;
        private CancellationTokenSource? _delaySource;

        internal IdleInhibitor(IInhibitor inhibitor, string reason)
        {
            _inhibitor = inhibitor;
            _reason = reason;
        }

        private async Task ResetDelay()
        {
            if (_delaySource != null)
            {
                var delaySource = _delaySource;
                _delaySource = null;

                await delaySource.CancelAsync().ConfigureAwait(false);
            }
        }

        private async Task InhibitInternal()
        {
            if (_releaseFunc == null) // TODO: avoid race
            {
                _releaseFunc = await _inhibitor.Inhibit(_reason).ConfigureAwait(false);
            }
        }

        public void Inhibit()
        {
            Task.Run(async () =>
            {
                await ResetDelay().ConfigureAwait(false);

                await InhibitInternal().ConfigureAwait(false);
            });
        }

        public void Inhibit(TimeSpan duration)
        {
            Task.Run(async () =>
            {
                await ResetDelay().ConfigureAwait(false);

                var delaySource = _delaySource = new CancellationTokenSource(duration);

                Task.Delay(-1, _delaySource.Token)
                    .ContinueWith(async (a) => {

                        // Check that this is a cancellation caused by the timeout

                        if (_delaySource == delaySource)
                        {
                            await UnInhibitInternal();
                        }
                    });

                await InhibitInternal().ConfigureAwait(false);
            });
        }

        private async Task UnInhibitInternal()
        {
            if (_releaseFunc != null)
            {
                var releaseFunc = _releaseFunc;
                _releaseFunc = null;

                await ResetDelay().ConfigureAwait(false);

                await releaseFunc().ConfigureAwait(false);
            }
        }

        public void UnInhibit()
        {
            Task.Run(UnInhibitInternal);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await UnInhibitInternal().ConfigureAwait(false);
        }
    }
}
