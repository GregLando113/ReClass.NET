using System;
using System.Runtime.InteropServices;
using ReClassNET.Native;

namespace ReClassNET.UI.Theming
{
	/// <summary>
	/// Wrappers around the (partly undocumented) Windows APIs which make native controls render in dark mode.
	/// All functions silently do nothing on systems which don't support them.
	/// </summary>
	internal static class DarkModeNative
	{
		private const int Windows10Build1809 = 17763;
		private const int Windows10Build20H1 = 18985;

		private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
		private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

		private enum PreferredAppMode
		{
			Default = 0,
			AllowDark = 1,
			ForceDark = 2,
			ForceLight = 3
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct OSVERSIONINFOEX
		{
			public int dwOSVersionInfoSize;
			public int dwMajorVersion;
			public int dwMinorVersion;
			public int dwBuildNumber;
			public int dwPlatformId;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string szCSDVersion;
			public ushort wServicePackMajor;
			public ushort wServicePackMinor;
			public ushort wSuiteMask;
			public byte wProductType;
			public byte wReserved;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int Left, Top, Right, Bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct COMBOBOXINFO
		{
			public int cbSize;
			public RECT rcItem;
			public RECT rcButton;
			public int stateButton;
			public IntPtr hwndCombo;
			public IntPtr hwndItem;
			public IntPtr hwndList;
		}

		[DllImport("ntdll.dll")]
		private static extern int RtlGetVersion(ref OSVERSIONINFOEX versionInfo);

		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

		[DllImport("uxtheme.dll", EntryPoint = "#133")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool AllowDarkModeForWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool allow);

		// On 1809 this ordinal is AllowDarkModeForApp(BOOL), which accepts the same values.
		[DllImport("uxtheme.dll", EntryPoint = "#135")]
		private static extern int SetPreferredAppMode(PreferredAppMode mode);

		[DllImport("uxtheme.dll", EntryPoint = "#136")]
		private static extern void FlushMenuThemes();

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO info);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOZORDER = 0x0004;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_FRAMECHANGED = 0x0020;

		private static readonly Lazy<int> buildNumber = new Lazy<int>(() =>
		{
			if (NativeMethods.IsUnix())
			{
				return 0;
			}

			try
			{
				var info = new OSVERSIONINFOEX { dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX)) };
				return RtlGetVersion(ref info) == 0 && info.dwMajorVersion >= 10 ? info.dwBuildNumber : 0;
			}
			catch
			{
				return 0;
			}
		});

		/// <summary>True if the system supports dark native controls (Windows 10 1809 or newer).</summary>
		public static bool IsSupported => buildNumber.Value >= Windows10Build1809;

		public static void SetAppDarkMode(bool dark)
		{
			if (!IsSupported)
			{
				return;
			}

			try
			{
				SetPreferredAppMode(dark ? PreferredAppMode.ForceDark : PreferredAppMode.Default);
				FlushMenuThemes();
			}
			catch
			{
				// ignored
			}
		}

		public static void SetTitleBarDarkMode(IntPtr hwnd, bool dark)
		{
			if (!IsSupported)
			{
				return;
			}

			try
			{
				AllowDarkModeForWindow(hwnd, dark);

				var value = dark ? 1 : 0;
				var attribute = buildNumber.Value >= Windows10Build20H1 ? DWMWA_USE_IMMERSIVE_DARK_MODE : DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
				DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

				// Force the non-client area to redraw with the new setting.
				SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
			}
			catch
			{
				// ignored
			}
		}

		/// <summary>Applies the given theme class to a window, or resets the window to the default theme if <paramref name="themeName"/> is null.</summary>
		public static void SetTheme(IntPtr hwnd, string themeName, bool allowDarkMode)
		{
			if (!IsSupported || hwnd == IntPtr.Zero)
			{
				return;
			}

			try
			{
				AllowDarkModeForWindow(hwnd, allowDarkMode);
				SetWindowTheme(hwnd, themeName, null);
			}
			catch
			{
				// ignored
			}
		}

		/// <summary>Disables visual styles for the window, so classic colors (e.g. PBM_SETBARCOLOR) are used.</summary>
		public static void DisableTheme(IntPtr hwnd)
		{
			if (!IsSupported || hwnd == IntPtr.Zero)
			{
				return;
			}

			try
			{
				SetWindowTheme(hwnd, string.Empty, string.Empty);
			}
			catch
			{
				// ignored
			}
		}

		/// <summary>Gets the drop-down list window of a combo box.</summary>
		public static IntPtr GetComboBoxListHandle(IntPtr comboBoxHandle)
		{
			if (NativeMethods.IsUnix())
			{
				return IntPtr.Zero;
			}

			try
			{
				var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO)) };
				return GetComboBoxInfo(comboBoxHandle, ref info) ? info.hwndList : IntPtr.Zero;
			}
			catch
			{
				return IntPtr.Zero;
			}
		}
	}
}
