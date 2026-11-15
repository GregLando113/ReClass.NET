using System.Drawing;
using System.Text;
using System.Windows.Forms;
using ReClassNET.UI.Theming;
using ReClassNET.Util;

namespace ReClassNET
{
	public class Settings
	{
		// Application Settings

		public string LastProcess { get; set; } = string.Empty;

		public bool StayOnTop { get; set; } = false;

		public bool RunAsAdmin { get; set; } = false;

		public bool RandomizeWindowTitle { get; set; } = false;

		// Node Drawing Settings

		public bool ShowNodeAddress { get; set; } = true;

		public bool ShowNodeOffset { get; set; } = true;

		public bool ShowNodeText { get; set; } = true;

		public bool HighlightChangedValues { get; set; } = true;

		public Encoding RawDataEncoding { get; set; } = Encoding.GetEncoding(1252); /* Windows-1252 */

		// Comment Drawing Settings

		public bool ShowCommentFloat { get; set; } = true;

		public bool ShowCommentInteger { get; set; } = true;

		public bool ShowCommentPointer { get; set; } = true;

		public bool ShowCommentRtti { get; set; } = true;

		public bool ShowCommentSymbol { get; set; } = true;

		public bool ShowCommentString { get; set; } = true;

		public bool ShowCommentPluginInfo { get; set; } = true;

		// Theme Settings

		/// <summary>The theme gets applied at startup, so a change needs a restart.</summary>
		public ThemeMode Theme { get; set; } = ThemeMode.Light;

		// Colors

		/// <summary>The node colors used with the light theme.</summary>
		public NodeColorScheme LightColors { get; } = NodeColorScheme.CreateLight();

		/// <summary>The node colors used with the dark theme.</summary>
		public NodeColorScheme DarkColors { get; } = NodeColorScheme.CreateDark();

		/// <summary>The node colors of the active theme.</summary>
		public NodeColorScheme Colors => Application.IsDarkModeEnabled ? DarkColors : LightColors;

		public Color BackgroundColor { get => Colors.Background; set => Colors.Background = value; }

		public Color SelectedColor { get => Colors.Selected; set => Colors.Selected = value; }

		public Color HiddenColor { get => Colors.Hidden; set => Colors.Hidden = value; }

		public Color OffsetColor { get => Colors.Offset; set => Colors.Offset = value; }

		public Color AddressColor { get => Colors.Address; set => Colors.Address = value; }

		public Color HexColor { get => Colors.Hex; set => Colors.Hex = value; }

		public Color TypeColor { get => Colors.Type; set => Colors.Type = value; }

		public Color NameColor { get => Colors.Name; set => Colors.Name = value; }

		public Color ValueColor { get => Colors.Value; set => Colors.Value = value; }

		public Color IndexColor { get => Colors.Index; set => Colors.Index = value; }

		public Color CommentColor { get => Colors.Comment; set => Colors.Comment = value; }

		public Color TextColor { get => Colors.Text; set => Colors.Text = value; }

		public Color VTableColor { get => Colors.VTable; set => Colors.VTable = value; }

		public Color PluginColor { get => Colors.Plugin; set => Colors.Plugin = value; }

		public CustomDataMap CustomData { get; } = new CustomDataMap();

		public Settings Clone() => MemberwiseClone() as Settings;
	}
}
