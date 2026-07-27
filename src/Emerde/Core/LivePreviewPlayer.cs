using LibVLCSharp.Shared;

namespace Emerde.Core;

public sealed class LivePreviewPlayer : IDisposable
{
    internal static readonly TimeSpan PlaybackStartTimeout = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan PlaybackStopTimeout = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan StandbyRetention = TimeSpan.FromSeconds(4);
    internal static readonly TimeSpan AudioTrackRestoreStep = TimeSpan.FromMilliseconds(10);
    internal static readonly TimeSpan AudioTrackRestoreTimeout = PlaybackStartTimeout;
    internal const int MaximumSessionCount = 3;
    internal const int CacheMilliseconds = 80;
    internal static readonly string[] LibVlcOptions =
    [
        $"--network-caching={CacheMilliseconds}",
        $"--live-caching={CacheMilliseconds}",
        $"--file-caching={CacheMilliseconds}",
        "--clock-jitter=0",
        "--clock-synchro=0",
        "--drop-late-frames",
        "--skip-frames",
    ];
    internal static readonly string[] MediaLowLatencyOptions =
    [
        $":network-caching={CacheMilliseconds}",
        $":live-caching={CacheMilliseconds}",
        $":file-caching={CacheMilliseconds}",
        ":clock-jitter=0",
        ":clock-synchro=0",
        ":drop-late-frames",
        ":skip-frames",
    ];

    private readonly object syncRoot = new();
    private readonly LibVLC libVlc;
    private readonly List<PreviewSession> sessions = [];
    private readonly HashSet<Task> pendingSessionDisposals = [];
    private Task sessionDisposalTail = Task.CompletedTask;
    private readonly System.Threading.Timer standbyTimer;
    private PreviewSession currentSession;
    private bool muted = true;
    private int volume;
    private bool disposed;

    public MediaPlayer MediaPlayer => Volatile.Read(ref currentSession).MediaPlayer;

    public LivePreviewFrameSource FrameSource => Volatile.Read(ref currentSession).FrameSource;

    public int StandbySessionCount
    {
        get
        {
            lock (syncRoot)
            {
                return Math.Max(0, sessions.Count - 1);
            }
        }
    }

    public event EventHandler? PlaybackFailed;

    public event EventHandler? PlaybackEnded;

    public event EventHandler? FrameSourceChanged;

    public event EventHandler? FirstFramePresented;

    public LivePreviewPlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        libVlc = new LibVLC(LibVlcOptions);
        currentSession = CreateSession(string.Empty);
        sessions.Add(currentSession);
        standbyTimer = new System.Threading.Timer(RemoveExpiredStandbySessions, null, 500, 500);
    }

    public async Task<bool> PlayAsync(
        string sessionKey,
        string url,
        string userAgent,
        string proxyUrl,
        string headers = "",
        CancellationToken cancellationToken = default,
        bool restartCurrentPlayback = false,
        bool allowStandbyReuse = true)
    {
        PreviewSession previousSession;
        PreviewSession targetSession;
        bool reusedSession;
        List<PreviewSession> removedSessions = [];

        lock (syncRoot)
        {
            ThrowIfDisposed();
            previousSession = currentSession;
            targetSession = allowStandbyReuse
                ? sessions.FirstOrDefault(session => session.IsReusable(sessionKey)) ?? CreatePlaybackSession(sessionKey, removedSessions)
                : CreatePlaybackSession(sessionKey, removedSessions);
            reusedSession = targetSession.IsReusable(sessionKey) && targetSession.HasMedia && !restartCurrentPlayback;
            targetSession.IsPinned = true;
            QueueSessionDisposals(removedSessions);
        }

        try
        {
            if (!reusedSession)
            {
                bool waitForPlaybackStart = !ReferenceEquals(previousSession, targetSession) || !restartCurrentPlayback;
                await targetSession.PlayAsync(url, userAgent, proxyUrl, headers, cancellationToken, waitForPlaybackStart);
            }

            cancellationToken.ThrowIfCancellationRequested();
            bool frameSourceChanged;
            lock (syncRoot)
            {
                ThrowIfDisposed();
                targetSession.IsPinned = false;
                targetSession.SessionKey = sessionKey;
                targetSession.LastUsedAt = DateTime.UtcNow;
                targetSession.ExpiresAt = DateTime.MaxValue;
                frameSourceChanged = !ReferenceEquals(currentSession, targetSession);
                if (frameSourceChanged)
                {
                    currentSession.ExpiresAt = DateTime.UtcNow + StandbyRetention;
                    currentSession.LastUsedAt = DateTime.UtcNow;
                    currentSession.BeginStandbyTransition();
                    currentSession = targetSession;
                }

                currentSession.Activate(volume, muted);
            }

            if (frameSourceChanged)
            {
                FrameSourceChanged?.Invoke(this, EventArgs.Empty);
            }

            if (targetSession.FrameSource.HasPresentedFrame)
            {
                FirstFramePresented?.Invoke(targetSession.FrameSource, EventArgs.Empty);
            }

            return reusedSession;
        }
        catch
        {
            lock (syncRoot)
            {
                targetSession.IsPinned = false;
                if (!ReferenceEquals(targetSession, currentSession))
                {
                    sessions.Remove(targetSession);
                }
            }

            if (!ReferenceEquals(targetSession, currentSession))
            {
                targetSession.Dispose();
            }
            throw;
        }
    }

    public void Stop()
    {
        PreviewSession[] stoppedSessions = ResetSessions();
        foreach (PreviewSession session in stoppedSessions)
        {
            session.Dispose();
        }
    }

    public async Task StopAsync()
    {
        PreviewSession[] stoppedSessions = ResetSessions();
        try
        {
            await Task.WhenAll(stoppedSessions.Select(session => session.StopAsync()));
        }
        finally
        {
            foreach (PreviewSession session in stoppedSessions)
            {
                session.Dispose();
            }
        }
    }

    public void DiscardStandbySessions()
    {
        List<PreviewSession> removedSessions;
        lock (syncRoot)
        {
            removedSessions = sessions.Where(session => !ReferenceEquals(session, currentSession)).ToList();
            foreach (PreviewSession session in removedSessions)
            {
                sessions.Remove(session);
            }
            QueueSessionDisposals(removedSessions);
        }
    }

    public void SetPaused(bool isPaused)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            currentSession.SetPaused(isPaused);
        }
    }

    public void SetMuted(bool isMuted)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            muted = isMuted;
            currentSession.SetMuted(isMuted);
        }
    }

    public void SetVolume(int value)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            volume = NormalizeVolume(value);
            currentSession.SetVolume(volume);
        }
    }

    internal static int NormalizeVolume(int value)
    {
        return Math.Clamp(value, 0, 100);
    }

    internal static int? SelectAudioTrackToRestore(
        int currentTrack,
        int? rememberedTrack,
        IEnumerable<int> availableTracks)
    {
        if (currentTrack >= 0)
        {
            return currentTrack;
        }

        if (rememberedTrack >= 0)
        {
            return rememberedTrack;
        }

        foreach (int availableTrack in availableTracks)
        {
            if (availableTrack >= 0)
            {
                return availableTrack;
            }
        }

        return null;
    }

    internal static bool ShouldWaitForPlaybackStart(bool replaceCurrentPlayback)
    {
        return !replaceCurrentPlayback;
    }

    internal static bool ShouldReuseSession(string currentKey, string targetKey, bool hasMedia, bool restartCurrentPlayback)
    {
        return !restartCurrentPlayback
            && hasMedia
            && !string.IsNullOrWhiteSpace(targetKey)
            && string.Equals(currentKey, targetKey, StringComparison.Ordinal);
    }

    internal static IReadOnlyList<string> SelectStandbyKeysToRemove(
        IReadOnlyList<(string Key, DateTime LastUsedAt, bool IsCurrent, bool IsPinned)> candidates,
        int maximumSessionCount)
    {
        int removeCount = Math.Max(0, candidates.Count - maximumSessionCount + 1);
        return candidates
            .Where(candidate => !candidate.IsCurrent && !candidate.IsPinned)
            .OrderBy(candidate => candidate.LastUsedAt)
            .Take(removeCount)
            .Select(candidate => candidate.Key)
            .ToArray();
    }

    public async Task<(uint Width, uint Height)?> ResolveVideoDimensionsAsync(CancellationToken cancellationToken = default)
    {
        PreviewSession session;
        lock (syncRoot)
        {
            if (disposed)
            {
                return null;
            }

            session = currentSession;
        }

        try
        {
            return await session.ResolveVideoDimensionsAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        PreviewSession[] disposedSessions;
        Task[] pendingDisposals;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            standbyTimer.Dispose();
            disposedSessions = sessions.ToArray();
            sessions.Clear();
            pendingDisposals = pendingSessionDisposals.ToArray();
        }

        foreach (PreviewSession session in disposedSessions)
        {
            session.Dispose();
        }
        bool completed;
        try
        {
            completed = Task.WaitAll(pendingDisposals, PlaybackStopTimeout);
        }
        catch (AggregateException)
        {
            completed = true;
        }

        if (completed)
        {
            libVlc.Dispose();
            return;
        }

        _ = Task.WhenAll(pendingDisposals).ContinueWith(
            _ => libVlc.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private PreviewSession CreatePlaybackSession(string sessionKey, List<PreviewSession> removedSessions)
    {
        if (sessions.Count == 1
            && ReferenceEquals(sessions[0], currentSession)
            && !currentSession.HasMedia)
        {
            currentSession.SessionKey = sessionKey;
            return currentSession;
        }

        IReadOnlyList<string> keysToRemove = SelectStandbyKeysToRemove(
            sessions.Select(session => (
                session.SessionKey,
                session.LastUsedAt,
                ReferenceEquals(session, currentSession),
                session.IsPinned)).ToArray(),
            MaximumSessionCount);
        foreach (string key in keysToRemove)
        {
            PreviewSession? removed = sessions.FirstOrDefault(session => string.Equals(session.SessionKey, key, StringComparison.Ordinal));
            if (removed != null)
            {
                sessions.Remove(removed);
                removedSessions.Add(removed);
            }
        }

        PreviewSession created = CreateSession(sessionKey);
        sessions.Add(created);
        return created;
    }

    private PreviewSession CreateSession(string sessionKey)
    {
        PreviewSession session = new(libVlc, sessionKey);
        session.FirstFramePresented += OnSessionFirstFramePresented;
        session.PlaybackFailed += OnSessionPlaybackFailed;
        session.PlaybackEnded += OnSessionPlaybackEnded;
        return session;
    }

    private PreviewSession[] ResetSessions()
    {
        PreviewSession[] stoppedSessions;
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (sessions.Count == 1 && ReferenceEquals(sessions[0], currentSession) && !currentSession.HasMedia)
            {
                return [];
            }

            stoppedSessions = sessions.ToArray();
            PreviewSession replacement = CreateSession(string.Empty);
            sessions.Clear();
            sessions.Add(replacement);
            currentSession = replacement;
        }

        FrameSourceChanged?.Invoke(this, EventArgs.Empty);
        return stoppedSessions;
    }

    private void RemoveExpiredStandbySessions(object? state)
    {
        List<PreviewSession> expired;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            expired = sessions
                .Where(session => !ReferenceEquals(session, currentSession) && !session.IsPinned && session.ExpiresAt <= now)
                .ToList();
            foreach (PreviewSession session in expired)
            {
                sessions.Remove(session);
            }
            QueueSessionDisposals(expired);
        }
    }

    private void QueueSessionDisposals(IReadOnlyCollection<PreviewSession> removedSessions)
    {
        if (removedSessions.Count == 0)
        {
            return;
        }

        PreviewSession[] sessionsToDispose = removedSessions.ToArray();
        Task cleanup = sessionDisposalTail.ContinueWith(
            _ =>
            {
                foreach (PreviewSession session in sessionsToDispose)
                {
                    session.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        sessionDisposalTail = cleanup;
        pendingSessionDisposals.Add(cleanup);
        _ = cleanup.ContinueWith(
            completed =>
            {
                lock (syncRoot)
                {
                    pendingSessionDisposals.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnSessionFirstFramePresented(object? sender, EventArgs e)
    {
        if (sender is PreviewSession session && ReferenceEquals(Volatile.Read(ref currentSession), session))
        {
            FirstFramePresented?.Invoke(session.FrameSource, EventArgs.Empty);
        }
    }

    private void OnSessionPlaybackFailed(object? sender, EventArgs e)
    {
        HandleSessionTermination(sender, PlaybackFailed);
    }

    private void OnSessionPlaybackEnded(object? sender, EventArgs e)
    {
        HandleSessionTermination(sender, PlaybackEnded);
    }

    private void HandleSessionTermination(object? sender, EventHandler? activeHandler)
    {
        if (sender is not PreviewSession session)
        {
            return;
        }

        bool isCurrent;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            isCurrent = ReferenceEquals(currentSession, session);
            if (!isCurrent)
            {
                sessions.Remove(session);
                QueueSessionDisposals([session]);
            }
        }

        if (isCurrent)
        {
            activeHandler?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed class PreviewSession : IDisposable
    {
        private readonly LibVLC libVlc;
        private readonly object audioSyncRoot = new();
        private Media? currentMedia;
        private EventHandler<EventArgs>? currentPlayingHandler;
        private EventHandler<EventArgs>? currentErrorHandler;
        private EventHandler<EventArgs>? currentEndReachedHandler;
        private long playbackSession;
        private int audioStateVersion;
        private int? activeAudioTrack;
        private bool desiredMuted = true;
        private int desiredVolume;
        private bool disposed;

        public PreviewSession(LibVLC libVlc, string sessionKey)
        {
            this.libVlc = libVlc;
            SessionKey = sessionKey;
            MediaPlayer = new MediaPlayer(libVlc)
            {
                Mute = true,
                Volume = 0,
            };
            FrameSource = new LivePreviewFrameSource(MediaPlayer);
            FrameSource.FirstFramePresented += OnFirstFramePresented;
        }

        public string SessionKey { get; set; }

        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } = DateTime.MaxValue;

        public bool IsPinned { get; set; }

        public bool HasMedia => currentMedia != null;

        public MediaPlayer MediaPlayer { get; }

        public LivePreviewFrameSource FrameSource { get; }

        public event EventHandler? PlaybackFailed;

        public event EventHandler? PlaybackEnded;

        public event EventHandler? FirstFramePresented;

        public bool IsReusable(string sessionKey)
        {
            return ShouldReuseSession(SessionKey, sessionKey, HasMedia, false)
                && MediaPlayer.State is VLCState.Playing or VLCState.Opening or VLCState.Buffering or VLCState.Paused;
        }

        public async Task PlayAsync(
            string url,
            string userAgent,
            string proxyUrl,
            string headers,
            CancellationToken cancellationToken,
            bool waitForPlaybackStart)
        {
            Media? previousMedia = null;
            try
            {
                if (currentMedia != null && MediaPlayer.State is not VLCState.Stopped and not VLCState.NothingSpecial)
                {
                    DetachPlaybackEvents();
                    previousMedia = currentMedia;
                    currentMedia = null;
                }
                else
                {
                    await StopAsync();
                }

                cancellationToken.ThrowIfCancellationRequested();
                Media media = CreateMedia(url, userAgent, proxyUrl, headers);
                currentMedia = media;
                MediaPlayer.AspectRatio = null;
                MediaPlayer.Scale = 0;
                TaskCompletionSource playbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                long session = Interlocked.Increment(ref playbackSession);
                int sessionStarted = 0;
                currentPlayingHandler = (_, _) =>
                {
                    if (Volatile.Read(ref playbackSession) != session)
                    {
                        return;
                    }

                    Interlocked.Exchange(ref sessionStarted, 1);
                    playbackStarted.TrySetResult();
                };
                currentErrorHandler = (_, _) =>
                {
                    if (Volatile.Read(ref playbackSession) != session)
                    {
                        return;
                    }

                    if (Volatile.Read(ref sessionStarted) == 0)
                    {
                        playbackStarted.TrySetException(new InvalidOperationException("Live preview playback failed."));
                        return;
                    }

                    PlaybackFailed?.Invoke(this, EventArgs.Empty);
                };
                currentEndReachedHandler = (_, _) =>
                {
                    if (Volatile.Read(ref playbackSession) != session)
                    {
                        return;
                    }

                    if (Volatile.Read(ref sessionStarted) == 0)
                    {
                        playbackStarted.TrySetException(new InvalidOperationException("Live preview playback ended before it started."));
                        return;
                    }

                    PlaybackEnded?.Invoke(this, EventArgs.Empty);
                };
                MediaPlayer.Playing += currentPlayingHandler;
                MediaPlayer.EncounteredError += currentErrorHandler;
                MediaPlayer.EndReached += currentEndReachedHandler;

                bool playAccepted = await Task.Run(() => MediaPlayer.Play(media), cancellationToken);
                if (!playAccepted)
                {
                    throw new InvalidOperationException("Live preview playback could not start.");
                }

                if (waitForPlaybackStart)
                {
                    Task completed = await Task.WhenAny(playbackStarted.Task, Task.Delay(PlaybackStartTimeout, cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(completed, playbackStarted.Task))
                    {
                        throw new TimeoutException("Live preview playback did not start in time.");
                    }

                    await playbackStarted.Task;
                }
            }
            catch
            {
                await StopAsync();
                throw;
            }
            finally
            {
                previousMedia?.Dispose();
            }
        }

        public async Task StopAsync()
        {
            DetachPlaybackEvents();
            if (MediaPlayer.State is not VLCState.Stopped and not VLCState.NothingSpecial)
            {
                TaskCompletionSource playbackStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<EventArgs> stoppedHandler = (_, _) => playbackStopped.TrySetResult();
                MediaPlayer.Stopped += stoppedHandler;
                try
                {
                    await Task.Run(() => MediaPlayer.Stop());
                    await Task.WhenAny(playbackStopped.Task, Task.Delay(PlaybackStopTimeout));
                }
                finally
                {
                    MediaPlayer.Stopped -= stoppedHandler;
                }
            }

            DisposeCurrentMedia();
        }

        public void SetMuted(bool isMuted)
        {
            lock (audioSyncRoot)
            {
                if (disposed)
                {
                    return;
                }

                int stateVersion = Interlocked.Increment(ref audioStateVersion);
                desiredMuted = isMuted;
                if (isMuted || desiredVolume == 0)
                {
                    MediaPlayer.Mute = true;
                    return;
                }

                ApplyActiveAudioState(desiredVolume, stateVersion);
            }
        }

        public void SetVolume(int value)
        {
            lock (audioSyncRoot)
            {
                if (disposed)
                {
                    return;
                }

                int stateVersion = Interlocked.Increment(ref audioStateVersion);
                int normalizedVolume = NormalizeVolume(value);
                desiredVolume = normalizedVolume;
                if (desiredMuted || normalizedVolume == 0)
                {
                    MediaPlayer.Volume = normalizedVolume;
                    if (normalizedVolume == 0)
                    {
                        MediaPlayer.Mute = true;
                    }
                    return;
                }

                ApplyActiveAudioState(normalizedVolume, stateVersion);
            }
        }

        public void SetPaused(bool isPaused)
        {
            MediaPlayer.SetPause(isPaused);
        }

        public void BeginStandbyTransition()
        {
            lock (audioSyncRoot)
            {
                if (disposed)
                {
                    return;
                }

                Interlocked.Increment(ref audioStateVersion);
                int audioTrack = MediaPlayer.AudioTrack;
                if (audioTrack >= 0)
                {
                    activeAudioTrack = audioTrack;
                }

                FrameSource.SetPresentationEnabled(false);
                MediaPlayer.Volume = 0;
                MediaPlayer.Mute = true;
                _ = MediaPlayer.SetAudioTrack(-1);
            }
        }

        public void Activate(int volume, bool muted)
        {
            lock (audioSyncRoot)
            {
                if (disposed)
                {
                    return;
                }

                int stateVersion = Interlocked.Increment(ref audioStateVersion);
                desiredMuted = muted;
                FrameSource.SetPresentationEnabled(true);
                MediaPlayer.SetPause(false);
                int normalizedVolume = NormalizeVolume(volume);
                desiredVolume = normalizedVolume;
                if (muted || normalizedVolume == 0)
                {
                    MediaPlayer.Mute = true;
                    MediaPlayer.Volume = normalizedVolume;
                    return;
                }

                ApplyActiveAudioState(normalizedVolume, stateVersion);
            }
        }

        private void ApplyActiveAudioState(int targetVolume, int stateVersion)
        {
            if (TryRestoreAudioTrack())
            {
                MediaPlayer.Volume = targetVolume;
                MediaPlayer.Mute = false;
                return;
            }

            MediaPlayer.Volume = 0;
            MediaPlayer.Mute = true;
            _ = RestoreActiveAudioAsync(targetVolume, stateVersion);
        }

        private async Task RestoreActiveAudioAsync(int targetVolume, int stateVersion)
        {
            try
            {
                int attemptCount = Math.Max(1, (int)Math.Ceiling(AudioTrackRestoreTimeout.TotalMilliseconds / AudioTrackRestoreStep.TotalMilliseconds));
                for (int attempt = 0; attempt < attemptCount; attempt++)
                {
                    lock (audioSyncRoot)
                    {
                        if (disposed || Volatile.Read(ref audioStateVersion) != stateVersion)
                        {
                            return;
                        }

                        if (TryRestoreAudioTrack())
                        {
                            MediaPlayer.Volume = targetVolume;
                            MediaPlayer.Mute = false;
                            return;
                        }
                    }

                    await Task.Delay(AudioTrackRestoreStep);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool TryRestoreAudioTrack()
        {
            int currentTrack = MediaPlayer.AudioTrack;
            int? audioTrack = SelectAudioTrackToRestore(
                currentTrack,
                activeAudioTrack,
                []);
            if (audioTrack is not int selectedTrack)
            {
                return TrySelectAvailableAudioTrack();
            }

            if (currentTrack == selectedTrack)
            {
                activeAudioTrack = null;
                return true;
            }

            if (!MediaPlayer.SetAudioTrack(selectedTrack))
            {
                activeAudioTrack = null;
                return TrySelectAvailableAudioTrack();
            }

            activeAudioTrack = null;
            return true;
        }

        private bool TrySelectAvailableAudioTrack()
        {
            foreach (int availableTrack in MediaPlayer.AudioTrackDescription.Select(track => track.Id))
            {
                if (availableTrack >= 0 && MediaPlayer.SetAudioTrack(availableTrack))
                {
                    activeAudioTrack = null;
                    return true;
                }
            }

            return false;
        }

        public void SetPresentationEnabled(bool enabled)
        {
            FrameSource.SetPresentationEnabled(enabled);
        }

        public async Task<(uint Width, uint Height)?> ResolveVideoDimensionsAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryGetVideoDimensions(out uint width, out uint height)
                    || TryGetVideoTrackDimensions(out width, out height))
                {
                    return (width, height);
                }

                await Task.Delay(100, cancellationToken);
            }

            string snapshotPath = Path.Combine(Path.GetTempPath(), $"Emerde-preview-{Guid.NewGuid():N}.png");
            try
            {
                bool captured = await Task.Run(() => MediaPlayer.TakeSnapshot(0, snapshotPath, 0, 0), cancellationToken);
                if (!captured)
                {
                    return null;
                }

                for (int attempt = 0; attempt < 30; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryReadSnapshotDimensions(snapshotPath, out uint width, out uint height))
                    {
                        return (width, height);
                    }

                    await Task.Delay(100, cancellationToken);
                }

                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
            finally
            {
                try
                {
                    File.Delete(snapshotPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        public void Dispose()
        {
            lock (audioSyncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                Interlocked.Increment(ref audioStateVersion);
            }

            DetachPlaybackEvents();
            FrameSource.FirstFramePresented -= OnFirstFramePresented;
            if (MediaPlayer.State is not VLCState.Stopped and not VLCState.NothingSpecial)
            {
                MediaPlayer.Stop();
            }
            DisposeCurrentMedia();
            FrameSource.Dispose();
            MediaPlayer.Dispose();
        }

        private void OnFirstFramePresented(object? sender, EventArgs e)
        {
            FirstFramePresented?.Invoke(this, EventArgs.Empty);
        }

        private void DetachPlaybackEvents()
        {
            Interlocked.Increment(ref playbackSession);
            if (currentPlayingHandler != null)
            {
                MediaPlayer.Playing -= currentPlayingHandler;
                currentPlayingHandler = null;
            }
            if (currentErrorHandler != null)
            {
                MediaPlayer.EncounteredError -= currentErrorHandler;
                currentErrorHandler = null;
            }
            if (currentEndReachedHandler != null)
            {
                MediaPlayer.EndReached -= currentEndReachedHandler;
                currentEndReachedHandler = null;
            }
        }

        private void DisposeCurrentMedia()
        {
            currentMedia?.Dispose();
            currentMedia = null;
        }

        private bool TryGetVideoDimensions(out uint width, out uint height)
        {
            width = 0;
            height = 0;
            return MediaPlayer.VoutCount > 0
                && MediaPlayer.Size(0, ref width, ref height)
                && width > 0
                && height > 0;
        }

        private bool TryGetVideoTrackDimensions(out uint width, out uint height)
        {
            width = 0;
            height = 0;
            Media? media = currentMedia;
            if (media == null)
            {
                return false;
            }

            try
            {
                foreach (MediaTrack track in media.Tracks)
                {
                    if (track.TrackType != TrackType.Video
                        || track.Data.Video.Width == 0
                        || track.Data.Video.Height == 0)
                    {
                        continue;
                    }

                    width = track.Data.Video.Width;
                    height = track.Data.Video.Height;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private Media CreateMedia(string url, string userAgent, string proxyUrl, string headers)
        {
            Media media = new(libVlc, new Uri(url));
            try
            {
                media.AddOption(":adaptive-logic=highest");
                foreach (string option in MediaLowLatencyOptions)
                {
                    media.AddOption(option);
                }

                string effectiveUserAgent = GetHeaderValue(headers, "User-Agent") ?? userAgent;
                if (!string.IsNullOrWhiteSpace(effectiveUserAgent))
                {
                    media.AddOption($":http-user-agent={effectiveUserAgent}");
                }

                string? referer = GetHeaderValue(headers, "Referer");
                if (!string.IsNullOrWhiteSpace(referer))
                {
                    media.AddOption($":http-referrer={referer}");
                }

                string? cookie = GetHeaderValue(headers, "Cookie");
                if (!string.IsNullOrWhiteSpace(cookie))
                {
                    media.AddOption($":http-cookie={cookie}");
                }

                string normalizedProxy = ProxyAddress.Normalize(proxyUrl);
                if (!string.IsNullOrWhiteSpace(normalizedProxy))
                {
                    media.AddOption($":http-proxy={normalizedProxy}");
                }

                return media;
            }
            catch
            {
                media.Dispose();
                throw;
            }
        }
    }

    internal static bool TryReadSnapshotDimensions(string snapshotPath, out uint width, out uint height)
    {
        width = 0;
        height = 0;
        try
        {
            FileInfo snapshot = new(snapshotPath);
            if (!snapshot.Exists || snapshot.Length == 0)
            {
                return false;
            }

            using FileStream stream = snapshot.OpenRead();
            System.Windows.Media.Imaging.BitmapDecoder decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            System.Windows.Media.Imaging.BitmapFrame? frame = decoder.Frames.FirstOrDefault();
            if (frame is not { PixelWidth: > 0, PixelHeight: > 0 })
            {
                return false;
            }

            width = (uint)frame.PixelWidth;
            height = (uint)frame.PixelHeight;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? GetHeaderValue(string headers, string name)
    {
        if (string.IsNullOrWhiteSpace(headers))
        {
            return null;
        }

        foreach (string line in headers.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            string headerName = line[..separator].Trim();
            if (string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase))
            {
                string value = line[(separator + 1)..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}
