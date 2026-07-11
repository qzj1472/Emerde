using LibVLCSharp.Shared;

namespace Emerde.Core;

public sealed class LivePreviewPlayer : IDisposable
{
    private readonly LibVLC libVlc;

    public MediaPlayer MediaPlayer { get; }

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

        using Media media = new(libVlc, new Uri(url));
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

        MediaPlayer.AspectRatio = null;
        MediaPlayer.Scale = 0;
        TaskCompletionSource playbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<EventArgs> playingHandler = (_, _) => playbackStarted.TrySetResult();
        EventHandler<EventArgs> errorHandler = (_, _) => playbackStarted.TrySetException(new InvalidOperationException("Live preview playback failed."));
        MediaPlayer.Playing += playingHandler;
        MediaPlayer.EncounteredError += errorHandler;

        try
        {
            bool playAccepted = await Task.Run(() => MediaPlayer.Play(media));
            if (!playAccepted)
            {
                throw new InvalidOperationException("Live preview playback could not start.");
            }

            Task completed = await Task.WhenAny(playbackStarted.Task, Task.Delay(TimeSpan.FromSeconds(3), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(completed, playbackStarted.Task))
            {
                await playbackStarted.Task;
            }
        }
        finally
        {
            MediaPlayer.Playing -= playingHandler;
            MediaPlayer.EncounteredError -= errorHandler;
        }
    }

    public void Stop()
    {
        if (MediaPlayer.State is not VLCState.Stopped and not VLCState.NothingSpecial)
        {
            MediaPlayer.Stop();
        }
    }

    public async Task StopAsync()
    {
        if (MediaPlayer.State is VLCState.Stopped or VLCState.NothingSpecial)
        {
            return;
        }

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
