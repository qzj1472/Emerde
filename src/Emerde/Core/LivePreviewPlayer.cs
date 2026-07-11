using LibVLCSharp.Shared;

namespace Emerde.Core;

public sealed class LivePreviewPlayer : IDisposable
{
    private static readonly TimeSpan PlaybackStartTimeout = TimeSpan.FromSeconds(8);
    private readonly LibVLC libVlc;
    private Media? currentMedia;
    private EventHandler<EventArgs>? currentPlayingHandler;
    private EventHandler<EventArgs>? currentErrorHandler;
    private EventHandler<EventArgs>? currentEndReachedHandler;
    private bool playbackStarted;

    public MediaPlayer MediaPlayer { get; }

    public event EventHandler? PlaybackFailed;

    public event EventHandler? PlaybackEnded;

    public LivePreviewPlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        libVlc = new LibVLC("--network-caching=1500", "--live-caching=1500", "--clock-jitter=0");
        MediaPlayer = new MediaPlayer(libVlc)
        {
            Mute = true,
            Volume = 80,
        };
    }

    public async Task PlayAsync(string url, string userAgent, string proxyUrl, string headers = "", CancellationToken cancellationToken = default)
    {
        await StopAsync();
        cancellationToken.ThrowIfCancellationRequested();

        Media media = CreateMedia(url, userAgent, proxyUrl, headers);
        currentMedia = media;

        MediaPlayer.AspectRatio = null;
        MediaPlayer.Scale = 0;
        TaskCompletionSource playbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        this.playbackStarted = false;
        currentPlayingHandler = (_, _) =>
        {
            this.playbackStarted = true;
            playbackStarted.TrySetResult();
        };
        currentErrorHandler = (_, _) =>
        {
            if (!this.playbackStarted)
            {
                playbackStarted.TrySetException(new InvalidOperationException("Live preview playback failed."));
                return;
            }

            PlaybackFailed?.Invoke(this, EventArgs.Empty);
        };
        currentEndReachedHandler = (_, _) =>
        {
            if (!this.playbackStarted)
            {
                playbackStarted.TrySetException(new InvalidOperationException("Live preview playback ended before it started."));
                return;
            }

            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        };
        MediaPlayer.Playing += currentPlayingHandler;
        MediaPlayer.EncounteredError += currentErrorHandler;
        MediaPlayer.EndReached += currentEndReachedHandler;

        try
        {
            bool playAccepted = await Task.Run(() => MediaPlayer.Play(media));
            if (!playAccepted)
            {
                throw new InvalidOperationException("Live preview playback could not start.");
            }

            Task completed = await Task.WhenAny(playbackStarted.Task, Task.Delay(PlaybackStartTimeout, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(completed, playbackStarted.Task))
            {
                throw new TimeoutException("Live preview playback did not start in time.");
            }

            await playbackStarted.Task;
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public void Stop()
    {
        DetachPlaybackEvents();
        if (MediaPlayer.State is not VLCState.Stopped and not VLCState.NothingSpecial)
        {
            MediaPlayer.Stop();
        }

        DisposeCurrentMedia();
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
                await Task.WhenAny(playbackStopped.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            }
            finally
            {
                MediaPlayer.Stopped -= stoppedHandler;
            }
        }

        DisposeCurrentMedia();
    }

    public void SetPaused(bool isPaused)
    {
        MediaPlayer.SetPause(isPaused);
    }

    public void SetMuted(bool isMuted)
    {
        MediaPlayer.Mute = isMuted;
    }

    public void Dispose()
    {
        Stop();
        MediaPlayer.Dispose();
        libVlc.Dispose();
    }

    private void DetachPlaybackEvents()
    {
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

        playbackStarted = false;
    }

    private void DisposeCurrentMedia()
    {
        currentMedia?.Dispose();
        currentMedia = null;
    }

    private Media CreateMedia(string url, string userAgent, string proxyUrl, string headers)
    {
        Media media = new(libVlc, new Uri(url));
        try
        {
            media.AddOption(":adaptive-logic=highest");

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
