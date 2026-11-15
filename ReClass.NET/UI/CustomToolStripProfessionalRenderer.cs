using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ReClassNET.UI.Theming;

namespace ReClassNET.UI
{
	internal class CustomToolStripProfessionalRenderer : ToolStripProfessionalRenderer
	{
		private readonly bool renderGrip;
		private readonly bool renderBorder;

		public CustomToolStripProfessionalRenderer(bool renderGrip, bool renderBorder)
			: base(new CustomProfessionalColorTable())
		{
			this.renderGrip = renderGrip;
			this.renderBorder = renderBorder;
		}

		protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
		{
			if (renderGrip)
			{
				base.OnRenderGrip(e);
			}
		}

		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
		{
			if (renderBorder)
			{
				base.OnRenderToolStripBorder(e);
			}
		}

		protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
		{
			//base.OnRenderToolStripPanelBackground(e);
		}

		protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
		{
			// The status strip background is drawn with the (light) visual style otherwise.
			if (ThemeManager.IsDark && e.ToolStrip is StatusStrip)
			{
				using var brush = new SolidBrush(ColorTable.StatusStripGradientBegin);
				e.Graphics.FillRectangle(brush, e.AffectedBounds);
				return;
			}

			base.OnRenderToolStripBackground(e);
		}

		protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
		{
			if (ThemeManager.IsDark && e.ToolStrip is StatusStrip statusStrip)
			{
				var bounds = statusStrip.SizeGripBounds;
				using var lightBrush = new SolidBrush(ColorTable.GripLight);
				using var darkBrush = new SolidBrush(ColorTable.GripDark);

				// Three rows of dots forming a triangle in the bottom right corner.
				for (var row = 0; row < 3; ++row)
				{
					for (var column = 0; column < 3; ++column)
					{
						if (row + column < 2)
						{
							continue;
						}

						var x = bounds.Right - 12 + column * 4;
						var y = bounds.Bottom - 12 + row * 4;
						e.Graphics.FillRectangle(darkBrush, x + 1, y + 1, 2, 2);
						e.Graphics.FillRectangle(lightBrush, x, y, 2, 2);
					}
				}
				return;
			}

			base.OnRenderStatusStripSizingGrip(e);
		}

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			if (ThemeManager.IsDark)
			{
				e.TextColor = e.Item.Enabled ? ThemeManager.Colors.Text : ThemeManager.Colors.DisabledText;
			}

			base.OnRenderItemText(e);
		}

		protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
		{
			if (ThemeManager.IsDark)
			{
				e.ArrowColor = e.Item?.Enabled != false ? ThemeManager.Colors.Text : ThemeManager.Colors.DisabledText;
			}

			base.OnRenderArrow(e);
		}

		protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
		{
			// The default check mark image is black and invisible on a dark background.
			if (ThemeManager.IsDark && e.Item is ToolStripMenuItem menuItem && menuItem.Image == null)
			{
				var rect = e.ImageRectangle;
				rect.Inflate(1, 1);

				var colorTable = (CustomProfessionalColorTable)ColorTable;
				using (var brush = new SolidBrush(menuItem.Selected ? colorTable.CheckSelectedBackground : colorTable.CheckBackground))
				{
					e.Graphics.FillRectangle(brush, rect);
				}
				using (var pen = new Pen(colorTable.ButtonSelectedBorder))
				{
					e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
				}

				var x = rect.Left + rect.Width / 2;
				var y = rect.Top + rect.Height / 2;
				var smoothingMode = e.Graphics.SmoothingMode;
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				using (var pen = new Pen(ThemeManager.Colors.Text, 1.6f))
				{
					e.Graphics.DrawLines(pen, new[] { new PointF(x - 4, y), new PointF(x - 1, y + 3), new PointF(x + 4, y - 3) });
				}
				e.Graphics.SmoothingMode = smoothingMode;

				return;
			}

			base.OnRenderItemCheck(e);
		}
	}

	internal class CustomProfessionalColorTable : ProfessionalColorTable
	{
		private static bool IsDark => ThemeManager.IsDark;

		private static readonly Color DarkBackground = ThemeColors.Dark.Window;
		private static readonly Color DarkMenuBackground = Color.FromArgb(43, 43, 43);
		private static readonly Color DarkBorder = Color.FromArgb(70, 70, 70);
		private static readonly Color DarkSelected = Color.FromArgb(65, 65, 68);
		private static readonly Color DarkSelectedBorder = Color.FromArgb(90, 90, 95);
		private static readonly Color DarkPressed = Color.FromArgb(85, 85, 90);
		private static readonly Color DarkChecked = Color.FromArgb(50, 70, 95);
		private static readonly Color DarkSeparator = Color.FromArgb(70, 70, 70);

		public override Color MenuStripGradientBegin => IsDark ? DarkBackground : SystemColors.Control;
		public override Color MenuStripGradientEnd => IsDark ? DarkBackground : SystemColors.Control;

		public override Color ToolStripGradientBegin => IsDark ? DarkBackground : SystemColors.Control;
		public override Color ToolStripGradientMiddle => IsDark ? DarkBackground : SystemColors.Control;
		public override Color ToolStripGradientEnd => IsDark ? DarkBackground : SystemColors.Control;
		public override Color ToolStripBorder => IsDark ? DarkBorder : base.ToolStripBorder;
		public override Color ToolStripDropDownBackground => IsDark ? DarkMenuBackground : base.ToolStripDropDownBackground;
		public override Color ToolStripContentPanelGradientBegin => IsDark ? DarkBackground : base.ToolStripContentPanelGradientBegin;
		public override Color ToolStripContentPanelGradientEnd => IsDark ? DarkBackground : base.ToolStripContentPanelGradientEnd;
		public override Color ToolStripPanelGradientBegin => IsDark ? DarkBackground : base.ToolStripPanelGradientBegin;
		public override Color ToolStripPanelGradientEnd => IsDark ? DarkBackground : base.ToolStripPanelGradientEnd;

		public override Color StatusStripGradientBegin => IsDark ? DarkBackground : base.StatusStripGradientBegin;
		public override Color StatusStripGradientEnd => IsDark ? DarkBackground : base.StatusStripGradientEnd;

		public override Color MenuBorder => IsDark ? DarkBorder : base.MenuBorder;
		public override Color MenuItemBorder => IsDark ? DarkSelectedBorder : base.MenuItemBorder;
		public override Color MenuItemSelected => IsDark ? DarkSelected : base.MenuItemSelected;
		public override Color MenuItemSelectedGradientBegin => IsDark ? DarkSelected : base.MenuItemSelectedGradientBegin;
		public override Color MenuItemSelectedGradientEnd => IsDark ? DarkSelected : base.MenuItemSelectedGradientEnd;
		public override Color MenuItemPressedGradientBegin => IsDark ? DarkMenuBackground : base.MenuItemPressedGradientBegin;
		public override Color MenuItemPressedGradientMiddle => IsDark ? DarkMenuBackground : base.MenuItemPressedGradientMiddle;
		public override Color MenuItemPressedGradientEnd => IsDark ? DarkMenuBackground : base.MenuItemPressedGradientEnd;

		public override Color ImageMarginGradientBegin => IsDark ? DarkMenuBackground : base.ImageMarginGradientBegin;
		public override Color ImageMarginGradientMiddle => IsDark ? DarkMenuBackground : base.ImageMarginGradientMiddle;
		public override Color ImageMarginGradientEnd => IsDark ? DarkMenuBackground : base.ImageMarginGradientEnd;
		public override Color ImageMarginRevealedGradientBegin => IsDark ? DarkMenuBackground : base.ImageMarginRevealedGradientBegin;
		public override Color ImageMarginRevealedGradientMiddle => IsDark ? DarkMenuBackground : base.ImageMarginRevealedGradientMiddle;
		public override Color ImageMarginRevealedGradientEnd => IsDark ? DarkMenuBackground : base.ImageMarginRevealedGradientEnd;

		public override Color ButtonSelectedBorder => IsDark ? DarkSelectedBorder : base.ButtonSelectedBorder;
		public override Color ButtonSelectedHighlight => IsDark ? DarkSelected : base.ButtonSelectedHighlight;
		public override Color ButtonSelectedHighlightBorder => IsDark ? DarkSelectedBorder : base.ButtonSelectedHighlightBorder;
		public override Color ButtonSelectedGradientBegin => IsDark ? DarkSelected : base.ButtonSelectedGradientBegin;
		public override Color ButtonSelectedGradientMiddle => IsDark ? DarkSelected : base.ButtonSelectedGradientMiddle;
		public override Color ButtonSelectedGradientEnd => IsDark ? DarkSelected : base.ButtonSelectedGradientEnd;
		public override Color ButtonPressedBorder => IsDark ? DarkSelectedBorder : base.ButtonPressedBorder;
		public override Color ButtonPressedHighlight => IsDark ? DarkPressed : base.ButtonPressedHighlight;
		public override Color ButtonPressedHighlightBorder => IsDark ? DarkSelectedBorder : base.ButtonPressedHighlightBorder;
		public override Color ButtonPressedGradientBegin => IsDark ? DarkPressed : base.ButtonPressedGradientBegin;
		public override Color ButtonPressedGradientMiddle => IsDark ? DarkPressed : base.ButtonPressedGradientMiddle;
		public override Color ButtonPressedGradientEnd => IsDark ? DarkPressed : base.ButtonPressedGradientEnd;
		public override Color ButtonCheckedHighlight => IsDark ? DarkChecked : base.ButtonCheckedHighlight;
		public override Color ButtonCheckedHighlightBorder => IsDark ? DarkSelectedBorder : base.ButtonCheckedHighlightBorder;
		public override Color ButtonCheckedGradientBegin => IsDark ? DarkChecked : base.ButtonCheckedGradientBegin;
		public override Color ButtonCheckedGradientMiddle => IsDark ? DarkChecked : base.ButtonCheckedGradientMiddle;
		public override Color ButtonCheckedGradientEnd => IsDark ? DarkChecked : base.ButtonCheckedGradientEnd;

		public override Color CheckBackground => IsDark ? DarkChecked : base.CheckBackground;
		public override Color CheckSelectedBackground => IsDark ? DarkPressed : base.CheckSelectedBackground;
		public override Color CheckPressedBackground => IsDark ? DarkPressed : base.CheckPressedBackground;

		public override Color SeparatorDark => IsDark ? DarkSeparator : base.SeparatorDark;
		public override Color SeparatorLight => IsDark ? DarkMenuBackground : base.SeparatorLight;

		public override Color GripDark => IsDark ? Color.FromArgb(25, 25, 25) : base.GripDark;
		public override Color GripLight => IsDark ? Color.FromArgb(90, 90, 90) : base.GripLight;

		public override Color OverflowButtonGradientBegin => IsDark ? DarkMenuBackground : base.OverflowButtonGradientBegin;
		public override Color OverflowButtonGradientMiddle => IsDark ? DarkMenuBackground : base.OverflowButtonGradientMiddle;
		public override Color OverflowButtonGradientEnd => IsDark ? DarkMenuBackground : base.OverflowButtonGradientEnd;

		public override Color RaftingContainerGradientBegin => IsDark ? DarkBackground : base.RaftingContainerGradientBegin;
		public override Color RaftingContainerGradientEnd => IsDark ? DarkBackground : base.RaftingContainerGradientEnd;
	}
}
