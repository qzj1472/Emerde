using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Emerde.Setup;

internal sealed class PreparationWindow : IDisposable
{
    private const int WindowWidth = 420;
    private const int WindowHeight = 168;
    private const uint WindowPopup = 0x80000000;
    private const uint WindowVisible = 0x10000000;
    private const uint WindowClipChildren = 0x02000000;
    private const uint WindowExToolWindow = 0x00000080;
    private const uint WindowMessageDestroy = 0x0002;
    private const uint WindowMessagePaint = 0x000F;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageTimer = 0x0113;
    private const int DrawTextSingleLine = 0x20;
    private const int DrawTextVerticalCenter = 0x4;
    private const int TransparentBackground = 1;
    private const int FontWeightNormal = 400;
    private const int FontWeightSemiBold = 600;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowBorderColor = 34;
    private const int DwmRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private static readonly WindowProcedure Procedure = WindowProc;
    private readonly ManualResetEventSlim ready = new();
    private Thread? thread;
    private IntPtr windowHandle;
    private int progressOffset;
    private bool closed;

    public void Show()
    {
        if (thread is not null)
        {
            return;
        }

        ActiveWindows[GetHashCode()] = this;
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Emerde Setup Preparation",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
    }

    public void Close()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        if (windowHandle != IntPtr.Zero)
        {
            PostMessage(windowHandle, WindowMessageClose, IntPtr.Zero, IntPtr.Zero);
        }

        thread?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Close();
        ActiveWindows.TryRemove(GetHashCode(), out _);
        ready.Dispose();
    }

    private void Run()
    {
        try
        {
            RunMessageLoop();
        }
        catch (Exception)
        {
        }
        finally
        {
            ready.Set();
            windowHandle = IntPtr.Zero;
        }
    }

    private void RunMessageLoop()
    {
        string className = $"EmerdeSetupPreparation{Environment.ProcessId}";
        IntPtr instance = GetModuleHandle(null);
        WindowClass windowClass = new()
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = instance,
            ClassName = className,
            Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)),
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            return;
        }

        try
        {
            SystemParametersInfo(48, 0, out Rectangle workArea, 0);
            int x = workArea.Left + (workArea.Right - workArea.Left - WindowWidth) / 2;
            int y = workArea.Top + (workArea.Bottom - workArea.Top - WindowHeight) / 2;
            windowHandle = CreateWindowEx(
                WindowExToolWindow,
                className,
                "Emerde 安装程序",
                WindowPopup | WindowVisible | WindowClipChildren,
                x,
                y,
                WindowWidth,
                WindowHeight,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);

            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            SetWindowLongPtr(windowHandle, -21, new IntPtr(GetHashCode()));
            int cornerPreference = DwmRound;
            uint borderColor = DwmColorNone;
            DwmSetWindowAttribute(windowHandle, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));
            DwmSetWindowAttribute(windowHandle, DwmWindowBorderColor, ref borderColor, sizeof(uint));
            SetTimer(windowHandle, UIntPtr.Zero, 24, IntPtr.Zero);
            ShowWindow(windowHandle, 5);
            UpdateWindow(windowHandle);
            ready.Set();

            while (GetMessage(out Message message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            UnregisterClass(className, instance);
        }
    }

    private static IntPtr WindowProc(IntPtr window, uint message, IntPtr wordParameter, IntPtr longParameter)
    {
        try
        {
            return ProcessWindowMessage(window, message, wordParameter, longParameter);
        }
        catch (Exception)
        {
            return DefWindowProc(window, message, wordParameter, longParameter);
        }
    }

    private static IntPtr ProcessWindowMessage(IntPtr window, uint message, IntPtr wordParameter, IntPtr longParameter)
    {
        PreparationWindow? owner = FindOwner(window);
        switch (message)
        {
            case WindowMessagePaint:
                if (owner is not null)
                {
                    owner.Paint(window);
                    return IntPtr.Zero;
                }
                break;
            case WindowMessageTimer:
                if (owner is not null)
                {
                    owner.progressOffset = (owner.progressOffset + 5) % 420;
                    InvalidateRect(window, IntPtr.Zero, false);
                }
                return IntPtr.Zero;
            case WindowMessageClose:
                DestroyWindow(window);
                return IntPtr.Zero;
            case WindowMessageDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(window, message, wordParameter, longParameter);
    }

    private static PreparationWindow? FindOwner(IntPtr window)
    {
        int hashCode = GetWindowLongPtr(window, -21).ToInt32();
        return ActiveWindows.GetValueOrDefault(hashCode);
    }

    private void Paint(IntPtr window)
    {
        BeginPaint(window, out PaintStructure paint);
        IntPtr deviceContext = paint.DeviceContext;
        IntPtr background = CreateSolidBrush(ToColorRef(0xF9, 0xFA, 0xFB));
        IntPtr track = CreateSolidBrush(ToColorRef(0xE1, 0xE4, 0xE8));
        IntPtr accent = CreateSolidBrush(ToColorRef(0x00, 0x78, 0xD4));
        IntPtr titleFont = CreateFont(
            -21, 0, 0, 0, FontWeightSemiBold, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        IntPtr bodyFont = CreateFont(
            -15, 0, 0, 0, FontWeightNormal, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        IntPtr previousFont = SelectObject(deviceContext, titleFont);

        try
        {
            FillRect(deviceContext, ref paint.PaintRectangle, background);
            SetBkMode(deviceContext, TransparentBackground);

            Rectangle titleRectangle = new(28, 30, WindowWidth - 28, 64);
            SetTextColor(deviceContext, ToColorRef(0x18, 0x1B, 0x1F));
            DrawText(deviceContext, "正在准备 Emerde", -1, ref titleRectangle, DrawTextSingleLine | DrawTextVerticalCenter);

            Rectangle bodyRectangle = new(28, 68, WindowWidth - 28, 100);
            SelectObject(deviceContext, bodyFont);
            SetTextColor(deviceContext, ToColorRef(0x60, 0x66, 0x70));
            DrawText(deviceContext, "正在校验并加载安装组件...", -1, ref bodyRectangle, DrawTextSingleLine | DrawTextVerticalCenter);

            Rectangle trackRectangle = new(28, 126, WindowWidth - 28, 130);
            FillRect(deviceContext, ref trackRectangle, track);
            int segmentWidth = 112;
            int left = 28 + progressOffset - segmentWidth;
            Rectangle accentRectangle = new(
                Math.Max(28, left),
                126,
                Math.Min(WindowWidth - 28, left + segmentWidth),
                130);
            if (accentRectangle.Right > accentRectangle.Left)
            {
                FillRect(deviceContext, ref accentRectangle, accent);
            }
        }
        finally
        {
            SelectObject(deviceContext, previousFont);
            DeleteObject(titleFont);
            DeleteObject(bodyFont);
            DeleteObject(background);
            DeleteObject(track);
            DeleteObject(accent);
            EndPaint(window, ref paint);
        }
    }

    private static int ToColorRef(byte red, byte green, byte blue) => red | green << 8 | blue << 16;

    private static readonly ConcurrentDictionary<int, PreparationWindow> ActiveWindows = new();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wordParameter, IntPtr longParameter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Value;
        public UIntPtr WordParameter;
        public IntPtr LongParameter;
        public uint Time;
        public Point Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStructure
    {
        public IntPtr DeviceContext;
        public int Erase;
        public Rectangle PaintRectangle;
        public int Restore;
        public int Update;
        public int Reserved0;
        public int Reserved1;
        public int Reserved2;
        public int Reserved3;
        public int Reserved4;
        public int Reserved5;
        public int Reserved6;
        public int Reserved7;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wordParameter, IntPtr longParameter);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wordParameter, IntPtr longParameter);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr window, UIntPtr eventId, uint interval, IntPtr callback);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr window, IntPtr rectangle, bool erase);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintStructure paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr window, ref PaintStructure paint);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, ref Rectangle rectangle, IntPtr brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr deviceContext, string text, int length, ref Rectangle rectangle, int format);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr deviceContext, int color);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out Rectangle value, uint flags);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint characterSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int size);
}
