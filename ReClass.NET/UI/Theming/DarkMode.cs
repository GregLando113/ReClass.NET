using System.Drawing;
using System.Windows.Forms;

namespace ReClassNET.UI.Theming
{
	/// <summary>Adjustments for the parts the dark color mode of Windows Forms doesn't handle on its own.</summary>
	public static class DarkMode
	{
		/// <summary>
		/// Returns a copy of the image where dark, colorless pixels (black text and outlines) are replaced
		/// with light ones if the dark theme is active. Otherwise the image is returned unchanged.
		/// </summary>
		public static Image AdaptImage(Image image)
		{
			if (image == null || !Application.IsDarkModeEnabled)
			{
				return image;
			}

			var bitmap = new Bitmap(image);
			for (var y = 0; y < bitmap.Height; ++y)
			{
				for (var x = 0; x < bitmap.Width; ++x)
				{
					var pixel = bitmap.GetPixel(x, y);
					if (pixel.A == 0 || pixel.GetBrightness() >= 0.5f || pixel.GetSaturation() >= 0.25f)
					{
						continue;
					}

					bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, 255 - pixel.R, 255 - pixel.G, 255 - pixel.B));
				}
			}
			return bitmap;
		}

		/// <summary>Fixes controls which keep their light look if the dark theme is active.</summary>
		public static void AdaptControls(Control parent)
		{
			if (!Application.IsDarkModeEnabled)
			{
				return;
			}

			foreach (Control control in parent.Controls)
			{
				switch (control)
				{
					// The visual style background of a tab page is always white.
					case TabPage tabPage:
						tabPage.UseVisualStyleBackColor = false;
						break;
					// The visual style headers are always light.
					case DataGridView dataGridView:
						dataGridView.EnableHeadersVisualStyles = false;
						break;
					// The default link colors are too dark.
					case LinkLabel linkLabel:
						linkLabel.LinkColor = Color.FromArgb(110, 175, 255);
						linkLabel.ActiveLinkColor = Color.FromArgb(160, 200, 255);
						linkLabel.VisitedLinkColor = Color.FromArgb(190, 160, 255);
						break;
					// The 3D border gets drawn as a thick white frame.
					case TextBox textBox when textBox.BorderStyle == BorderStyle.Fixed3D:
						textBox.BorderStyle = BorderStyle.FixedSingle;
						break;
				}

				AdaptControls(control);
			}
		}
	}
}
