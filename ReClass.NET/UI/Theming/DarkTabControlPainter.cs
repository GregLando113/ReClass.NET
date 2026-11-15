using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReClassNET.UI.Theming
{
	/// <summary>
	/// Subclasses a <see cref="TabControl"/> and paints the tab strip and page border with the theme colors.
	/// The native tab control can't be colored otherwise.
	/// </summary>
	internal sealed class DarkTabControlPainter : NativeWindow, IDisposable
	{
		private const int WM_PAINT = 0x000F;
		private const int WM_ERASEBKGND = 0x0014;
		private const int WM_PRINTCLIENT = 0x0318;

		[StructLayout(LayoutKind.Sequential)]
		private struct PAINTSTRUCT
		{
			public IntPtr hdc;
			public bool fErase;
			public int left, top, right, bottom;
			public bool fRestore;
			public bool fIncUpdate;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
			public byte[] rgbReserved;
		}

		[DllImport("user32.dll")]
		private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT paintStruct);

		[DllImport("user32.dll")]
		private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT paintStruct);

		private readonly TabControl tabControl;
		private int hotIndex = -1;

		public DarkTabControlPainter(TabControl tabControl)
		{
			this.tabControl = tabControl;

			tabControl.HandleCreated += OnHandleCreated;
			tabControl.HandleDestroyed += OnHandleDestroyed;
			tabControl.MouseMove += OnMouseMove;
			tabControl.MouseLeave += OnMouseLeave;

			if (tabControl.IsHandleCreated)
			{
				AssignHandle(tabControl.Handle);
			}

			tabControl.Invalidate();
		}

		public void Dispose()
		{
			tabControl.HandleCreated -= OnHandleCreated;
			tabControl.HandleDestroyed -= OnHandleDestroyed;
			tabControl.MouseMove -= OnMouseMove;
			tabControl.MouseLeave -= OnMouseLeave;

			ReleaseHandle();

			tabControl.Invalidate();
		}

		private void OnHandleCreated(object sender, EventArgs e) => AssignHandle(tabControl.Handle);

		private void OnHandleDestroyed(object sender, EventArgs e) => ReleaseHandle();

		private void OnMouseMove(object sender, MouseEventArgs e)
		{
			var index = -1;
			for (var i = 0; i < tabControl.TabCount; ++i)
			{
				if (tabControl.GetTabRect(i).Contains(e.Location))
				{
					index = i;
					break;
				}
			}

			if (index != hotIndex)
			{
				hotIndex = index;
				tabControl.Invalidate();
			}
		}

		private void OnMouseLeave(object sender, EventArgs e)
		{
			if (hotIndex != -1)
			{
				hotIndex = -1;
				tabControl.Invalidate();
			}
		}

		protected override void WndProc(ref Message m)
		{
			switch (m.Msg)
			{
				case WM_ERASEBKGND:
					m.Result = (IntPtr)1;
					return;
				case WM_PAINT:
					if (m.WParam != IntPtr.Zero)
					{
						PaintToHdc(m.WParam);
					}
					else
					{
						var hdc = BeginPaint(m.HWnd, out var ps);
						try
						{
							PaintToHdc(hdc);
						}
						finally
						{
							EndPaint(m.HWnd, ref ps);
						}
					}
					m.Result = IntPtr.Zero;
					return;
				case WM_PRINTCLIENT:
					PaintToHdc(m.WParam);
					m.Result = IntPtr.Zero;
					return;
			}

			base.WndProc(ref m);
		}

		private void PaintToHdc(IntPtr hdc)
		{
			var bounds = tabControl.ClientRectangle;
			if (bounds.Width <= 0 || bounds.Height <= 0)
			{
				return;
			}

			using var target = Graphics.FromHdc(hdc);
			using var buffer = BufferedGraphicsManager.Current.Allocate(target, bounds);

			Paint(buffer.Graphics, bounds);

			buffer.Render(target);
		}

		private void Paint(Graphics g, Rectangle bounds)
		{
			var colors = ThemeManager.Colors;

			using (var brush = new SolidBrush(colors.Window))
			{
				g.FillRectangle(brush, bounds);
			}

			if (tabControl.TabCount == 0)
			{
				return;
			}

			var pageTop = tabControl.Alignment == TabAlignment.Top ? tabControl.GetTabRect(0).Bottom : 0;
			var pageBottom = tabControl.Alignment == TabAlignment.Bottom ? tabControl.GetTabRect(0).Top : bounds.Bottom;
			var pageBounds = new Rectangle(0, pageTop, bounds.Width - 1, pageBottom - pageTop - 1);

			using var borderPen = new Pen(colors.Border);

			g.DrawRectangle(borderPen, pageBounds);

			var selectedIndex = tabControl.SelectedIndex;

			for (var i = 0; i < tabControl.TabCount; ++i)
			{
				if (i != selectedIndex)
				{
					PaintTab(g, i, false, borderPen, colors);
				}
			}
			if (selectedIndex >= 0)
			{
				PaintTab(g, selectedIndex, true, borderPen, colors);
			}
		}

		private void PaintTab(Graphics g, int index, bool selected, Pen borderPen, ThemeColors colors)
		{
			var page = tabControl.TabPages[index];
			var rect = tabControl.GetTabRect(index);
			var isTop = tabControl.Alignment != TabAlignment.Bottom;

			if (selected)
			{
				// The selected tab is drawn slightly larger and merges with the page.
				rect.Inflate(2, 0);
				if (isTop)
				{
					rect.Y -= 2;
					rect.Height += 3;
				}
				else
				{
					rect.Y -= 1;
					rect.Height += 3;
				}
			}

			var back = selected ? colors.Window : index == hotIndex ? colors.Hover : colors.Header;
			using (var brush = new SolidBrush(back))
			{
				g.FillRectangle(brush, rect);
			}

			// Left, right and outer edge. The edge next to the page stays open for the selected tab.
			g.DrawLine(borderPen, rect.Left, rect.Top, rect.Left, rect.Bottom - 1);
			g.DrawLine(borderPen, rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom - 1);
			if (isTop)
			{
				g.DrawLine(borderPen, rect.Left, rect.Top, rect.Right - 1, rect.Top);
			}
			else
			{
				g.DrawLine(borderPen, rect.Left, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
			}

			var contentRect = rect;
			contentRect.Inflate(-3, 0);

			var image = GetTabImage(page);
			if (image != null)
			{
				var imageY = rect.Top + (rect.Height - image.Height) / 2;
				g.DrawImage(image, contentRect.Left, imageY, image.Width, image.Height);
				contentRect.X += image.Width + 2;
				contentRect.Width -= image.Width + 2;
			}

			var textColor = !tabControl.Enabled ? colors.DisabledText : selected ? colors.Text : Blend(colors.Text, back, 0.8f);
			TextRenderer.DrawText(
				g,
				page.Text,
				tabControl.Font,
				contentRect,
				textColor,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
			);
		}

		private Image GetTabImage(TabPage page)
		{
			var imageList = tabControl.ImageList;
			if (imageList == null)
			{
				return null;
			}

			if (page.ImageIndex >= 0 && page.ImageIndex < imageList.Images.Count)
			{
				return imageList.Images[page.ImageIndex];
			}
			if (!string.IsNullOrEmpty(page.ImageKey) && imageList.Images.ContainsKey(page.ImageKey))
			{
				return imageList.Images[page.ImageKey];
			}
			return null;
		}

		private static Color Blend(Color foreground, Color background, float amount)
		{
			return Color.FromArgb(
				(int)(foreground.R * amount + background.R * (1 - amount)),
				(int)(foreground.G * amount + background.G * (1 - amount)),
				(int)(foreground.B * amount + background.B * (1 - amount))
			);
		}
	}
}
