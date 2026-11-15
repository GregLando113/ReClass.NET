using System.Drawing;

namespace ReClassNET.UI.Theming
{
	/// <summary>The colors used to draw the application chrome (not the memory view nodes).</summary>
	public sealed class ThemeColors
	{
		/// <summary>Background of forms, panels and tab pages.</summary>
		public Color Window { get; private set; }

		/// <summary>Background of input and list controls.</summary>
		public Color Surface { get; private set; }

		/// <summary>Background of headers and inactive tabs.</summary>
		public Color Header { get; private set; }

		public Color Text { get; private set; }

		public Color DisabledText { get; private set; }

		public Color PlaceholderText { get; private set; }

		public Color Border { get; private set; }

		public Color GridLine { get; private set; }

		public Color Hover { get; private set; }

		public Color Pressed { get; private set; }

		public Color Selection { get; private set; }

		public Color SelectionText { get; private set; }

		public Color Link { get; private set; }

		public Color ActiveLink { get; private set; }

		public Color VisitedLink { get; private set; }

		/// <summary>Text color for errors and changed values.</summary>
		public Color Error { get; private set; }

		/// <summary>Text color for positive/highlighted information (e.g. relative addresses).</summary>
		public Color Success { get; private set; }

		public static ThemeColors Light { get; } = new ThemeColors
		{
			Window = SystemColors.Control,
			Surface = SystemColors.Window,
			Header = SystemColors.Control,
			Text = SystemColors.ControlText,
			DisabledText = SystemColors.GrayText,
			PlaceholderText = SystemColors.ControlDarkDark,
			Border = SystemColors.ControlDark,
			GridLine = SystemColors.ControlDark,
			Hover = SystemColors.ControlLight,
			Pressed = SystemColors.ControlDark,
			Selection = SystemColors.Highlight,
			SelectionText = SystemColors.HighlightText,
			Link = Color.FromArgb(0, 0, 255),
			ActiveLink = Color.Red,
			VisitedLink = Color.FromArgb(128, 0, 128),
			Error = Color.Red,
			Success = Color.ForestGreen
		};

		public static ThemeColors Dark { get; } = new ThemeColors
		{
			Window = Color.FromArgb(32, 32, 32),
			Surface = Color.FromArgb(43, 43, 43),
			Header = Color.FromArgb(51, 51, 51),
			Text = Color.FromArgb(230, 230, 230),
			DisabledText = Color.FromArgb(128, 128, 128),
			PlaceholderText = Color.FromArgb(140, 140, 140),
			Border = Color.FromArgb(80, 80, 80),
			GridLine = Color.FromArgb(62, 62, 62),
			Hover = Color.FromArgb(62, 62, 64),
			Pressed = Color.FromArgb(82, 82, 86),
			Selection = Color.FromArgb(0, 95, 184),
			SelectionText = Color.White,
			Link = Color.FromArgb(110, 175, 255),
			ActiveLink = Color.FromArgb(160, 200, 255),
			VisitedLink = Color.FromArgb(190, 160, 255),
			Error = Color.FromArgb(255, 105, 105),
			Success = Color.FromArgb(110, 200, 110)
		};
	}
}
