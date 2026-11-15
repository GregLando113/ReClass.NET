using System.Drawing;

namespace ReClassNET
{
	/// <summary>The colors used to draw the nodes in the memory view.</summary>
	public class NodeColorScheme
	{
		public Color Background { get; set; }

		public Color Selected { get; set; }

		public Color Hidden { get; set; }

		public Color Offset { get; set; }

		public Color Address { get; set; }

		public Color Hex { get; set; }

		public Color Type { get; set; }

		public Color Name { get; set; }

		public Color Value { get; set; }

		public Color Index { get; set; }

		public Color Comment { get; set; }

		public Color Text { get; set; }

		public Color VTable { get; set; }

		public Color Plugin { get; set; }

		public static NodeColorScheme CreateLight() => new NodeColorScheme
		{
			Background = Color.FromArgb(255, 255, 255),
			Selected = Color.FromArgb(240, 240, 240),
			Hidden = Color.FromArgb(240, 240, 240),
			Offset = Color.FromArgb(255, 0, 0),
			Address = Color.FromArgb(0, 200, 0),
			Hex = Color.FromArgb(0, 0, 0),
			Type = Color.FromArgb(0, 0, 255),
			Name = Color.FromArgb(32, 32, 128),
			Value = Color.FromArgb(255, 128, 0),
			Index = Color.FromArgb(32, 200, 200),
			Comment = Color.FromArgb(0, 200, 0),
			Text = Color.FromArgb(0, 0, 255),
			VTable = Color.FromArgb(0, 255, 0),
			Plugin = Color.FromArgb(255, 0, 255)
		};

		public static NodeColorScheme CreateDark() => new NodeColorScheme
		{
			Background = Color.FromArgb(30, 30, 30),
			Selected = Color.FromArgb(52, 52, 56),
			Hidden = Color.FromArgb(52, 52, 56),
			Offset = Color.FromArgb(240, 113, 113),
			Address = Color.FromArgb(106, 196, 106),
			Hex = Color.FromArgb(212, 212, 212),
			Type = Color.FromArgb(86, 156, 214),
			Name = Color.FromArgb(156, 220, 254),
			Value = Color.FromArgb(230, 150, 70),
			Index = Color.FromArgb(78, 201, 176),
			Comment = Color.FromArgb(106, 153, 85),
			Text = Color.FromArgb(214, 157, 133),
			VTable = Color.FromArgb(128, 224, 128),
			Plugin = Color.FromArgb(218, 112, 214)
		};
	}
}
