using System.Windows;
using System.Windows.Interop;
using StudentApp.ViewModels;

namespace StudentApp.Views;

public partial class StudentShellWindow : Window
{
    private const double ScreenPadding = 12;

    public StudentShellWindow(StudentShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => EnsureWindowFitsCurrentScreen();
    }

    private void EnsureWindowFitsCurrentScreen()
    {
        var helper = new WindowInteropHelper(this);
        var monitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return;
        }

        var monitorInfo = NativeMethods.CreateMonitorInfo();
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var workArea = monitorInfo.rcWork.ToRect();
        var availableWidth = Math.Max(MinWidth, workArea.Width - (ScreenPadding * 2));
        var availableHeight = Math.Max(MinHeight, workArea.Height - (ScreenPadding * 2));

        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);

        var desiredLeft = Left;
        var desiredTop = Top;

        if (double.IsNaN(desiredLeft) || WindowStartupLocation == WindowStartupLocation.CenterScreen)
        {
            desiredLeft = workArea.Left + ((workArea.Width - Width) / 2);
        }

        if (double.IsNaN(desiredTop) || WindowStartupLocation == WindowStartupLocation.CenterScreen)
        {
            desiredTop = workArea.Top + ((workArea.Height - Height) / 2);
        }

        Left = Math.Clamp(desiredLeft, workArea.Left + ScreenPadding, workArea.Right - Width - ScreenPadding);
        Top = Math.Clamp(desiredTop, workArea.Top + ScreenPadding, workArea.Bottom - Height - ScreenPadding);
    }

    private static class NativeMethods
    {
        public const uint MonitorDefaultToNearest = 2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

        public static MonitorInfo CreateMonitorInfo()
        {
            return new MonitorInfo
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>()
            };
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Rect ToRect()
        {
            return new Rect(Left, Top, Right - Left, Bottom - Top);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public RectNative rcMonitor;
        public RectNative rcWork;
        public int dwFlags;
    }
}
