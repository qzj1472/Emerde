using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Toolkit.Uwp.Notifications;
using NAudio.Wave;
using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace Emerde.Core;

internal static class Notifier
{
    static Notifier()
    {
        ToastNotificationManagerCompat.OnActivated += (ToastNotificationActivatedEventArgsCompat e) =>
        {
            WeakReferenceMessenger.Default.Send(new ToastNotificationActivatedMessage(e));
        };
    }

    public static void AddNotice(string header, string title, string detail = null!, ToastDuration duration = ToastDuration.Short)
    {
        if (Environment.OSVersion.Version.Major < 10)
        {
            return;
        }

        new ToastContentBuilder()
            .AddHeader("AddNotice", header, "AddNotice")
            .AddText(title)
            .AddAttributionTextIf(!string.IsNullOrEmpty(detail), detail)
            .SetToastDuration(duration)
            .Show();
    }

    public static void AddNoticeWithButton(string header, string title, ToastContentButtonOption[] buttons, ToastDuration duration = ToastDuration.Short)
    {
        if (Environment.OSVersion.Version.Major < 10)
        {
            return;
        }

        new ToastContentBuilder()
            .AddHeader("AddNotice", header, "AddNotice")
            .AddText(title)
            .AddButtons(buttons)
            .SetToastDuration(duration)
            .Show();
    }

    public static void ClearNotice()
    {
        if (Environment.OSVersion.Version.Major < 10)
        {
            return;
        }

        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch
        {
        }
    }

    public static async Task PlayMusicAsync(Stream stream)
    {
        using MusicPlayer player = new(stream);
        using CancellationTokenSource source = new(TimeSpan.FromSeconds(30));
        player.Play();
        try
        {
            await player.WaitAsync(source.Token);
        }
        catch (OperationCanceledException)
        {
            player.Stop();
        }
    }

    public static async Task<bool> SendEmailAsync(string smtpServer, int port, string userName, string password, string nickName, string roomUrl, CancellationToken token = default)
    {
        try
        {
            using MailMessage mail = new();
            mail.From = new MailAddress(userName);
            mail.To.Add(userName);
            string safeNickName = HtmlEncoder.Default.Encode(nickName);
            string safeRoomUrl = HtmlEncoder.Default.Encode(roomUrl);
            mail.Subject = $"{nickName}{Locale.Culture.WordSpace()}{"LiveNotification".Tr()} - Emerde";
            mail.Body = $"<html><body>{"MailBodyElement".Tr(safeNickName)} <a href=\"{safeRoomUrl}\">{safeRoomUrl}</a></body></html>";
            mail.IsBodyHtml = true;

            using SmtpClient smtp = new(smtpServer, Math.Clamp(port, 1, 65535));
            smtp.Credentials = new NetworkCredential(userName, password);
            smtp.EnableSsl = true;
            smtp.Timeout = 15000;
            await smtp.SendMailAsync(mail).WaitAsync(TimeSpan.FromSeconds(15), token);

            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppSessionLogger.Event("error", "notification", "email_failed", ex.Message, new { smtpServer, port, userName });
        }
        return false;
    }
}

internal sealed class ToastNotificationActivatedMessage(ToastNotificationActivatedEventArgsCompat e)
{
    public ToastNotificationActivatedEventArgsCompat EventArgs { get; } = e;
}

public sealed class ToastContentButtonOption
{
    public string Content { get; set; } = string.Empty;

    public (string key, string value)[] Arguments { get; set; } = [];

    public ToastActivationType ActivationType { get; set; } = ToastActivationType.Background;
}

file static class ToastContentBuilderExtensions
{
    public static ToastContentBuilder AddAttributionTextIf(this ToastContentBuilder builder, bool condition, string text)
    {
        if (condition)
        {
            return builder.AddAttributionText(text);
        }
        else
        {
            return builder;
        }
    }

    public static ToastContentBuilder AddButtons(this ToastContentBuilder builder, params ToastContentButtonOption[] buttonOptions)
    {
        foreach (ToastContentButtonOption buttonOption in buttonOptions)
        {
            ToastButton button = new ToastButton()
                .SetContent(buttonOption.Content)
                .AddArguments(buttonOption.Arguments);

            button.ActivationType = buttonOption.ActivationType;
            builder.AddButton(button);
        }
        return builder;
    }
}

file static class ToastButtonExtensions
{
    public static ToastButton AddArguments(this ToastButton toastButton, (string, string)[] args)
    {
        foreach ((string, string) arg in args)
        {
            toastButton.AddArgument(arg.Item1, arg.Item2);
        }
        return toastButton;
    }
}

file sealed partial class MusicPlayer : IDisposable
{
    private bool disposed = false;
    private readonly Mp3FileReader mp3FileReader;
    private readonly WaveOut waveOut;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MusicPlayer(Stream stream)
    {
        mp3FileReader = new Mp3FileReader(stream);
        waveOut = new WaveOut();
        waveOut.Init(mp3FileReader);
        waveOut.PlaybackStopped += OnPlaybackStopped;
    }

    public void Play()
    {
        waveOut.Play();
    }

    public void Stop()
    {
        waveOut.Stop();
        mp3FileReader.Position = 0;
    }

    public void Pause()
    {
        waveOut.Stop();
    }

    public async Task WaitAsync(CancellationToken token = default)
    {
        await completion.Task.WaitAsync(token);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception == null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(e.Exception);
        }
    }

    public void Closed()
    {
        Dispose();
    }

    public void Dispose()
    {
        CleanUp(true);
        GC.SuppressFinalize(this);
    }

    private void CleanUp(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                waveOut.PlaybackStopped -= OnPlaybackStopped;
                waveOut.Dispose();
                mp3FileReader.Dispose();
            }
        }
        disposed = true;
    }
}
