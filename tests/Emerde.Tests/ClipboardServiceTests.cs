using System.Runtime.InteropServices;
using Emerde.Core;

namespace Emerde.Tests;

public sealed class ClipboardServiceTests
{
    [Fact]
    public async Task SetTextAsync_RetriesClipboardContentionUpToSixAttempts()
    {
        int attempts = 0;

        bool result = await ClipboardService.SetTextAsync("value", _ =>
        {
            attempts++;
            if (attempts < 6)
            {
                throw new ExternalException();
            }
        }, TimeSpan.Zero, 6);

        Assert.True(result);
        Assert.Equal(6, attempts);
    }

    [Fact]
    public async Task SetTextAsync_ReturnsFalseAfterAllAttemptsFail()
    {
        int attempts = 0;

        bool result = await ClipboardService.SetTextAsync("value", _ =>
        {
            attempts++;
            throw new ExternalException();
        }, TimeSpan.Zero, 6);

        Assert.False(result);
        Assert.Equal(6, attempts);
    }

    [Fact]
    public async Task SetTextAsync_RejectsEmptyTextWithoutTouchingClipboard()
    {
        int attempts = 0;

        bool result = await ClipboardService.SetTextAsync(" ", _ => attempts++, TimeSpan.Zero, 6);

        Assert.False(result);
        Assert.Equal(0, attempts);
    }
}
