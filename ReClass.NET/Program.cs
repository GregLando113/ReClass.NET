using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using ReClassNET.Core;
using ReClassNET.Extensions;
using ReClassNET.Forms;
using ReClassNET.Logger;
using ReClassNET.Memory;
using ReClassNET.Native;
using ReClassNET.UI;
using ReClassNET.UI.Theming;
using ReClassNET.Util;

namespace ReClassNET
{
	public static class Program
	{
		public static CommandLineArgs CommandLineArgs { get; private set; }

		public static Settings Settings { get; private set; }

		public static ILogger Logger { get; private set; }

		public static Random GlobalRandom { get; } = new Random();

		public static RemoteProcess RemoteProcess { get; private set; }

		public static CoreFunctionsManager CoreFunctions => RemoteProcess.CoreFunctions;

		public static MainForm MainForm { get; private set; }

		public static bool DesignMode { get; private set; } = true;

		public static FontEx MonoSpaceFont { get; private set; }

		[STAThread]
		static void Main(string[] args)
		{
			DesignMode = false; // The designer doesn't call Main()

			CommandLineArgs = new CommandLineArgs(args);

			// Code page encodings like Windows-1252 are not available by default.
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

			Settings = SettingsSerializer.Load();

			// Applies the high DPI mode, visual styles and default font configured in the project file.
			// This and the color mode must be set before the first window handle or Graphics object gets created.
			ApplicationConfiguration.Initialize();

			Application.SetColorMode(Settings.Theme switch
			{
				ThemeMode.Dark => SystemColorMode.Dark,
				ThemeMode.System => SystemColorMode.System,
				_ => SystemColorMode.Classic
			});

			DpiUtil.TrySetDpiFromCurrentDesktop();

			MonoSpaceFont = new FontEx
			{
				Font = new Font("Courier New", DpiUtil.ScaleIntX(13), GraphicsUnit.Pixel),
				Width = DpiUtil.ScaleIntX(8),
				Height = DpiUtil.ScaleIntY(16)
			};

			NativeMethods.EnableDebugPrivileges();

			Logger = new GuiLogger();

			if (Settings.RunAsAdmin && !WinUtil.IsAdministrator)
			{
				WinUtil.RunElevated(Process.GetCurrentProcess().MainModule?.FileName, args.Length > 0 ? string.Join(" ", args) : null);
				return;
			}

#if !DEBUG
			try
			{
#endif
			using (var coreFunctions = new CoreFunctionsManager())
			{
				RemoteProcess = new RemoteProcess(coreFunctions);

				MainForm = new MainForm();

				Application.Run(MainForm);

				RemoteProcess.Dispose();
			}
#if !DEBUG
			}
			catch (Exception ex)
			{
				ShowException(ex);
			}
#endif

			SettingsSerializer.Save(Settings);
		}

		/// <summary>Shows the exception in a dialog with expandable details.</summary>
		/// <param name="ex">The exception.</param>
		public static void ShowException(Exception ex)
		{
			var details = ex.ToString();

			var copyButton = new TaskDialogButton("Copy details")
			{
				AllowCloseDialog = false
			};
			copyButton.Click += (s, e) => Clipboard.SetText(details);

			var page = new TaskDialogPage
			{
				Caption = Constants.ApplicationName,
				Heading = ex.GetType().Name,
				Text = ex.GetMessageChain(),
				Icon = TaskDialogIcon.Error,
				AllowCancel = true,
				SizeToContent = true,
				Expander = new TaskDialogExpander(details)
				{
					CollapsedButtonText = "Show details",
					ExpandedButtonText = "Hide details",
					Position = TaskDialogExpanderPosition.AfterFootnote
				},
				Buttons = { copyButton, TaskDialogButton.Close }
			};

			var owner = Form.ActiveForm;
			if (owner != null)
			{
				TaskDialog.ShowDialog(owner, page);
			}
			else
			{
				TaskDialog.ShowDialog(page);
			}
		}
	}
}
