using System;
using System.Diagnostics.Contracts;
using System.Drawing;

namespace ReClassNET.Native
{
	public static class NativeMethods
	{
		private static readonly INativeMethods nativeMethods = new NativeMethodsWindows();

		public static IntPtr LoadLibrary(string name)
		{
			Contract.Requires(name != null);

			return nativeMethods.LoadLibrary(name);
		}

		public static IntPtr GetProcAddress(IntPtr handle, string name)
		{
			Contract.Requires(name != null);

			return nativeMethods.GetProcAddress(handle, name);
		}

		public static void FreeLibrary(IntPtr handle)
		{
			nativeMethods.FreeLibrary(handle);
		}

		public static Icon GetIconForFile(string path)
		{
			Contract.Requires(path != null);

			return nativeMethods.GetIconForFile(path);
		}

		public static void EnableDebugPrivileges()
		{
			nativeMethods.EnableDebugPrivileges();
		}

		public static string UndecorateSymbolName(string name)
		{
			Contract.Requires(name != null);
			Contract.Ensures(Contract.Result<string>() != null);

			return nativeMethods.UndecorateSymbolName(name);
		}

		public static bool RegisterExtension(string fileExtension, string extensionId, string applicationPath, string applicationName)
		{
			Contract.Requires(!string.IsNullOrEmpty(fileExtension));
			Contract.Requires(!string.IsNullOrEmpty(extensionId));
			Contract.Requires(applicationPath != null);
			Contract.Requires(applicationName != null);

			return nativeMethods.RegisterExtension(fileExtension, extensionId, applicationPath, applicationName);
		}

		public static void UnregisterExtension(string fileExtension, string extensionId)
		{
			Contract.Requires(!string.IsNullOrEmpty(fileExtension));
			Contract.Requires(!string.IsNullOrEmpty(extensionId));

			nativeMethods.UnregisterExtension(fileExtension, extensionId);
		}
	}
}
