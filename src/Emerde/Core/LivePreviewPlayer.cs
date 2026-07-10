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

    public async Task PlayAsync(string url, string userAgent, string proxyUrl)
    {
        await StopAsync();

        using Media media = new(libVlc, new Uri(url));

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            media.AddOption($":http-user-agent={userAgent}");
        }

        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            media.AddOption($":http-proxy=http://{proxyUrl}");
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
            if (!MediaPlayer.Play(media))
            {
                throw new InvalidOperationException("Live preview playback could not start.");
            }

            Task completed = await Task.WhenAny(playbackStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));
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
            MediaPlayer.Stop();
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
}
