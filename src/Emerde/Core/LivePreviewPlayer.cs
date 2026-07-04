using LibVLCSharp.Shared;

namespace Emerde.Core;

public sealed class LivePreviewPlayer : IDisposable
{
    private readonly LibVLC libVlc;

    public MediaPlayer MediaPlayer { get; }

    public LivePreviewPlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        libVlc = new LibVLC("--network-caching=1000", "--live-caching=1000", "--clock-jitter=0", "--drop-late-frames", "--skip-frames");
        MediaPlayer = new MediaPlayer(libVlc)
        {
            Mute = true,
            Volume = 80,
        };
    }

    public void Play(string url, string userAgent, string proxyUrl)
    {
        Stop();

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
        MediaPlayer.Play(media);
    }

    public void Stop()
    {
        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Stop();
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
