using Emerde.Views;
using System.Windows.Media.Animation;

namespace Emerde.Tests;

public sealed class DialogBlurScopeTests
{
    [Fact]
    public void BlurEntrance_UsesAProgressiveEaseInOutTransition()
    {
        DoubleAnimation animation = DialogBlurScope.CreateBlurEntranceAnimation(0d, 8d);

        Assert.Equal(0d, animation.From);
        Assert.Equal(8d, animation.To);
        Assert.Equal(TimeSpan.FromMilliseconds(DialogBlurScope.BlurEntranceDurationMilliseconds), animation.Duration.TimeSpan);
        Assert.Equal(FillBehavior.HoldEnd, animation.FillBehavior);
        SineEase easing = Assert.IsType<SineEase>(animation.EasingFunction);
        Assert.Equal(EasingMode.EaseInOut, easing.EasingMode);
    }

    [Fact]
    public void BackdropEntrance_UsesASeparateSoftFade()
    {
        DoubleAnimation animation = DialogBlurScope.CreateBackdropEntranceAnimation(1d);

        Assert.Equal(0d, animation.From);
        Assert.Equal(1d, animation.To);
        Assert.Equal(TimeSpan.FromMilliseconds(DialogBlurScope.BackdropEntranceDurationMilliseconds), animation.Duration.TimeSpan);
        Assert.Equal(FillBehavior.HoldEnd, animation.FillBehavior);
        SineEase easing = Assert.IsType<SineEase>(animation.EasingFunction);
        Assert.Equal(EasingMode.EaseOut, easing.EasingMode);
    }

    [Fact]
    public void Exit_UsesAShortEaseInTransitionFromTheCurrentVisualState()
    {
        DoubleAnimation animation = DialogBlurScope.CreateExitAnimation(8d, 0d);

        Assert.Equal(8d, animation.From);
        Assert.Equal(0d, animation.To);
        Assert.Equal(TimeSpan.FromMilliseconds(DialogBlurScope.ExitDurationMilliseconds), animation.Duration.TimeSpan);
        Assert.Equal(FillBehavior.HoldEnd, animation.FillBehavior);
        SineEase easing = Assert.IsType<SineEase>(animation.EasingFunction);
        Assert.Equal(EasingMode.EaseIn, easing.EasingMode);
    }

    [Fact]
    public void StartupVisualTreePumps_HaveStrictTimeBounds()
    {
        Assert.InRange(DialogBlurScope.OwnerEnablePumpMaximumTicks, 1, 20);
        Assert.InRange(DialogBlurScope.DialogMaskClearPumpMaximumTicks, 1, 20);
        Assert.True(DialogBlurScope.OwnerEnablePumpMaximumTicks * 50 <= 1000);
        Assert.True(DialogBlurScope.DialogMaskClearPumpMaximumTicks * 25 <= 500);
    }

    [Fact]
    public void ClosingDialog_StopsStartupPumpsWithoutClearingTheBackdropEarly()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "DialogBlurScope.cs"));
        int methodStart = source.IndexOf("private void ContentDialogClosing", StringComparison.Ordinal);
        int animationStart = source.IndexOf("isExitAnimating = true", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && animationStart > methodStart);
        string preparation = source[methodStart..animationStart];
        Assert.Contains("ownerEnableTimer?.Stop()", preparation);
        Assert.Contains("dialogMaskClearTimer?.Stop()", preparation);
        Assert.DoesNotContain("ClearDialogMaskVisuals(sender)", preparation);
        Assert.DoesNotContain("EnableOwnerWindow(ownerWindow)", preparation);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
