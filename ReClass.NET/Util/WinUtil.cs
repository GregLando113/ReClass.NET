using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Security.Principal;

namespace ReClassNET.Util
{
	public static class WinUtil
	{
		//from https://stackoverflow.com/a/11660205
		public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

		/// <summary>Executes the a process with elevated permissions.</summary>
		/// <param name="applicationPath"> The executable path.</param>
		/// <param name="arguments">The arguments.</param>
		/// <returns>True if it succeeds, false if it fails.</returns>
		public static bool RunElevated(string applicationPath, string arguments)
		{
			Contract.Requires(applicationPath != null);

			try
			{
				var processStartInfo = new ProcessStartInfo
				{
					FileName = applicationPath,
					UseShellExecute = true,
					WindowStyle = ProcessWindowStyle.Normal,
					Verb = "runas"
				};
				if (arguments != null)
				{
					processStartInfo.Arguments = arguments;
				}

				Process.Start(processStartInfo);
			}
			catch (Exception)
			{
				return false;
			}

			return true;
		}
	}
}
