using System;
using System.Runtime.InteropServices;
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

    private readonly IIdleInhibitor _inhibitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdleInhibitorManager"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger.</param>
    public IdleInhibitorManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<IdleInhibitorManager>();
        _inhibitor = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new IOPMInhibitor() : new DbusLoginManagerInhibitor();
    }

    public IdleInhibitor CreateInhibitor(InhibitorType type, string reason)
    {
        return new IdleInhibitor(_inhibitor, type, reason);
    }

    public sealed class IdleInhibitor : IAsyncDisposable
    {
        private readonly TaskFactory _runQueue = new(new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1).ExclusiveScheduler);
        private readonly IIdleInhibitor _inhibitor;
        private readonly InhibitorType _type;
        private readonly string _reason;

        private Func<Task>? _releaseFunc;
        private CancellationTokenSource? _delaySource;

        internal IdleInhibitor(IIdleInhibitor inhibitor, InhibitorType type, string reason)
        {
            _inhibitor = inhibitor;
            _type = type;
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
            if (_releaseFunc == null)
            {
                _releaseFunc = await _inhibitor.Inhibit(_type, _reason).ConfigureAwait(false);
            }
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2008:Do not create tasks without passing a TaskScheduler", Justification = "There is a TaskScheduler")]
        public void Inhibit()
        {
            _runQueue.StartNew(async () =>
            {
                await ResetDelay().ConfigureAwait(false);

                await InhibitInternal().ConfigureAwait(false);
            });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2008:Do not create tasks without passing a TaskScheduler", Justification = "There is a TaskScheduler")]
        public void Inhibit(TimeSpan duration)
        {
            TaskFactory factory = new TaskFactory(TaskScheduler.Current);

            _runQueue.StartNew(async () =>
            {
                await ResetDelay().ConfigureAwait(false);

                var delaySource = _delaySource = new CancellationTokenSource(duration);

                _ = factory.StartNew(async () =>
                {
                    try
                    {
                        await Task.Delay(-1, _delaySource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException e)
                    {
                        _ = e; // Ignore
                    }

                    // Check that this is a cancellation caused by the timeout

                    _ = _runQueue.StartNew(async () =>
                    {
                        if (_delaySource == delaySource)
                        {
                            await UnInhibitInternal().ConfigureAwait(false);
                        }
                    });
                });

                await InhibitInternal().ConfigureAwait(false);
            });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2008:Do not create tasks without passing a TaskScheduler", Justification = "There is a TaskScheduler")]
        public void UnInhibit()
        {
            _runQueue.StartNew(UnInhibitInternal);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await UnInhibitInternal().ConfigureAwait(false);
        }
    }
}
