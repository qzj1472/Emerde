using System.ComponentModel;
using System.Globalization;
using System.Windows;
using Emerde.Core;

namespace Emerde.Views;

public sealed partial class DiskSpaceAlertWindow : Window
{
    private bool allowClose;

    internal DiskSpaceAlertWindow(Window owner)
    {
        Owner = owner;
        InitializeComponent();
    }

    internal void UpdateState(StorageProtectionState state)
    {
        TitleText.Text = "StorageExhaustedTitle".Tr();
        MessageText.Text = "StorageExhaustedMessage".Tr();
        CurrentSpaceText.Text = "StorageExhaustedCurrentSpace".Tr(FormatBytes(state.MinimumAvailableBytes));
        RecoveryText.Text = "StorageExhaustedRecovery".Tr();
    }

    internal void CloseAfterRecovery()
    {
        allowClose = true;
        if (IsVisible)
        {
            Close();
        }
    }

    internal void CloseForShutdown()
    {
        allowClose = true;
        if (IsVisible)
        {
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose && GlobalMonitor.IsStorageExhausted)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private static string FormatBytes(long? bytes)
    {
        return bytes is long value && value >= 0
            ? value.ToString("N0", CultureInfo.CurrentCulture) + " bytes"
            : "unknown";
    }
}
