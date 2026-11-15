using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Microsoft.Win32;
using ReClassNET.Controls;
using ReClassNET.Native;

namespace ReClassNET.UI.Theming
{
	/// <summary>
	/// Applies the light or dark theme to all windows registered in the <see cref="GlobalWindowManager"/>.
	/// Every property changed for the dark theme is recorded, so switching back to light restores the original state.
	/// </summary>
	public static class ThemeManager
	{
		/// <summary>Restore actions per themed object, keyed by the changed property.</summary>
		private static readonly ConditionalWeakTable<object, Dictionary<string, Action>> restoreActions = new ConditionalWeakTable<object, Dictionary<string, Action>>();

		/// <summary>Controls which already have the event handlers attached.</summary>
		private static readonly ConditionalWeakTable<Control, object> hookedControls = new ConditionalWeakTable<Control, object>();

		/// <summary>Controls with a native window theme applied, which needs to be reset when switching to light.</summary>
		private static readonly ConditionalWeakTable<Control, object> nativeThemedControls = new ConditionalWeakTable<Control, object>();

		private static readonly ConditionalWeakTable<ToolTip, object> nativeThemedToolTips = new ConditionalWeakTable<ToolTip, object>();

		private static readonly ConditionalWeakTable<TabControl, DarkTabControlPainter> tabPainters = new ConditionalWeakTable<TabControl, DarkTabControlPainter>();

		private static readonly object marker = new object();

		private static readonly ToolStripRenderer darkToolStripRenderer = new CustomToolStripProfessionalRenderer(true, true);
		private static ToolStripRenderer lightToolStripRenderer;

		private static bool isInitialized;

		/// <summary>True if the dark theme is currently active.</summary>
		public static bool IsDark { get; private set; }

		/// <summary>The colors of the active theme.</summary>
		public static ThemeColors Colors => IsDark ? ThemeColors.Dark : ThemeColors.Light;

		/// <summary>Raised after the active theme changed and all windows were updated.</summary>
		public static event EventHandler ThemeChanged;

		/// <summary>Resolves the theme setting and starts theming all windows. Must be called on the UI thread.</summary>
		public static void Initialize()
		{
			if (isInitialized)
			{
				return;
			}
			isInitialized = true;

			lightToolStripRenderer = ToolStripManager.Renderer;

			GlobalWindowManager.WindowAdded += (s, e) => ApplyTheme(e.Form);

			if (!NativeMethods.IsUnix())
			{
				SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
			}

			IsDark = ResolveIsDark(Program.Settings.Theme);
			if (IsDark)
			{
				ApplyApplicationTheme();
			}
		}

		/// <summary>Applies the application wide settings (native menus, default tool strip renderer).</summary>
		private static void ApplyApplicationTheme()
		{
			DarkModeNative.SetAppDarkMode(IsDark);

			// Context menus and tool strips without a custom renderer use the manager renderer.
			ToolStripManager.Renderer = IsDark ? darkToolStripRenderer : lightToolStripRenderer;
		}

		/// <summary>Re-evaluates the theme setting and updates all open windows if the active theme changed.</summary>
		public static void Refresh()
		{
			var isDark = ResolveIsDark(Program.Settings.Theme);
			if (isDark == IsDark)
			{
				return;
			}

			IsDark = isDark;

			ApplyApplicationTheme();

			foreach (var form in GlobalWindowManager.Windows.ToList())
			{
				ApplyTheme(form);
				form.Invalidate(true);
			}

			ThemeChanged?.Invoke(null, EventArgs.Empty);
		}

		private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
		{
			if (Program.Settings.Theme != ThemeMode.System)
			{
				return;
			}

			var mainForm = Program.MainForm;
			if (mainForm != null && mainForm.IsHandleCreated && mainForm.InvokeRequired)
			{
				mainForm.BeginInvoke((Action)Refresh);
			}
			else
			{
				Refresh();
			}
		}

		private static bool ResolveIsDark(ThemeMode mode)
		{
			switch (mode)
			{
				case ThemeMode.Dark:
					return true;
				case ThemeMode.System:
					return !SystemInformation.HighContrast && IsWindowsAppThemeDark();
				default:
					return false;
			}
		}

		private static bool IsWindowsAppThemeDark()
		{
			if (NativeMethods.IsUnix())
			{
				return false;
			}

			try
			{
				using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
				return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
			}
			catch
			{
				return false;
			}
		}

		#region Control Theming

		/// <summary>Applies the active theme to the control and all of its children.</summary>
		public static void ApplyTheme(Control control)
		{
			if (control == null)
			{
				return;
			}

			HookControl(control);

			if (IsDark)
			{
				ApplyDark(control);
			}
			else
			{
				RestoreLight(control);
			}

			ApplyNativeTheme(control);

			foreach (Control child in control.Controls)
			{
				ApplyTheme(child);
			}
		}

		private static void HookControl(Control control)
		{
			if (hookedControls.TryGetValue(control, out _))
			{
				return;
			}
			hookedControls.Add(control, marker);

			// Controls added later (e.g. dynamically created rows) need to be themed too.
			control.ControlAdded += (s, e) =>
			{
				if (IsDark || restoreActions.TryGetValue(control, out _))
				{
					ApplyTheme(e.Control);
				}
			};

			// Native themes are lost when the handle gets recreated.
			control.HandleCreated += (s, e) => ApplyNativeTheme(control);

			switch (control)
			{
				case GroupBox groupBox:
					groupBox.Paint += OnGroupBoxPaint;
					break;
				case DataGridView dataGridView:
					dataGridView.Paint += OnDataGridViewPaint;
					break;
				case UpDownBase upDownBase:
					upDownBase.Paint += OnUpDownBasePaint;
					break;
				case RadioButton radioButton:
					radioButton.Paint += OnRadioButtonPaint;
					break;
				case Control c when IsUpDownButtons(c):
					c.Paint += OnUpDownButtonsPaint;
					break;
			}
		}

		private static void ApplyDark(Control control)
		{
			var colors = ThemeColors.Dark;

			switch (control)
			{
				case MemoryViewControl _:
				case BannerBox _:
					// These controls draw themselves.
					return;
				case Form _:
				case TabPage _:
				case SplitContainer _:
				case SplitterPanel _:
				case ToolStripPanel _:
					SetBackColor(control, colors.Window);
					SetForeColor(control, colors.Text);
					break;
				case TextBoxBase _:
				case ListBox _:
				case ComboBox _:
				case TreeView _:
				case ListView _:
				case UpDownBase _:
				case DataGridView _:
					SetBackColor(control, colors.Surface);
					SetForeColor(control, colors.Text);
					break;
				case ButtonBase _:
				case Label _:
				case GroupBox _:
				case Panel _:
				case UserControl _:
					// Inherit the background from the parent, but use the light text color.
					SetForeColor(control, colors.Text);
					break;
			}

			switch (control)
			{
				case TabControl tabControl:
					if (!tabPainters.TryGetValue(tabControl, out _))
					{
						tabPainters.Add(tabControl, new DarkTabControlPainter(tabControl));
					}
					break;
				case TabPage tabPage:
					Set(tabPage, nameof(TabPage.UseVisualStyleBackColor), tabPage.UseVisualStyleBackColor, false, v => tabPage.UseVisualStyleBackColor = v);
					break;
				case Button button when button.Image != null || button.BackgroundImage != null:
					// Buttons with images can't use the native (system) style.
					Set(button, nameof(Button.FlatStyle), button.FlatStyle, FlatStyle.Flat, v => button.FlatStyle = v);
					SetBackColor(button, colors.Header);
					Set(button, "FlatAppearance.BorderColor", button.FlatAppearance.BorderColor, colors.Border, v => button.FlatAppearance.BorderColor = v);
					Set(button, "FlatAppearance.MouseOverBackColor", button.FlatAppearance.MouseOverBackColor, colors.Hover, v => button.FlatAppearance.MouseOverBackColor = v);
					Set(button, "FlatAppearance.MouseDownBackColor", button.FlatAppearance.MouseDownBackColor, colors.Pressed, v => button.FlatAppearance.MouseDownBackColor = v);
					break;
				case RadioButton radioButton when IsOwnerDrawableRadioButton(radioButton):
					// The native dark radio button draws black text, so it is painted in OnRadioButtonPaint.
					break;
				case ButtonBase buttonBase when !(buttonBase is CheckBox checkBox && checkBox.Appearance == Appearance.Button):
					// The native controls support dark mode (see ApplyNativeTheme).
					Set(buttonBase, nameof(ButtonBase.FlatStyle), buttonBase.FlatStyle, FlatStyle.System, v => buttonBase.FlatStyle = v);
					break;
				case LinkLabel linkLabel:
					Set(linkLabel, nameof(LinkLabel.LinkColor), linkLabel.LinkColor, colors.Link, v => linkLabel.LinkColor = v);
					Set(linkLabel, nameof(LinkLabel.ActiveLinkColor), linkLabel.ActiveLinkColor, colors.ActiveLink, v => linkLabel.ActiveLinkColor = v);
					Set(linkLabel, nameof(LinkLabel.VisitedLinkColor), linkLabel.VisitedLinkColor, colors.VisitedLink, v => linkLabel.VisitedLinkColor = v);
					break;
				case PlaceholderTextBox placeholderTextBox:
					SetBorderStyle(placeholderTextBox);
					Set(placeholderTextBox, nameof(PlaceholderTextBox.PlaceholderColor), placeholderTextBox.PlaceholderColor, colors.PlaceholderText, v => placeholderTextBox.PlaceholderColor = v);
					break;
				case TextBox textBox:
					SetBorderStyle(textBox);
					break;
				case ListBox listBox:
					if (listBox.BorderStyle == BorderStyle.Fixed3D)
					{
						Set(listBox, nameof(ListBox.BorderStyle), listBox.BorderStyle, BorderStyle.FixedSingle, v => listBox.BorderStyle = v);
					}
					break;
				case TreeView treeView:
					if (treeView.BorderStyle == BorderStyle.Fixed3D)
					{
						Set(treeView, nameof(TreeView.BorderStyle), treeView.BorderStyle, BorderStyle.FixedSingle, v => treeView.BorderStyle = v);
					}
					Set(treeView, nameof(TreeView.LineColor), treeView.LineColor, colors.Border, v => treeView.LineColor = v);
					break;
				case UpDownBase upDown:
					if (upDown.BorderStyle == BorderStyle.Fixed3D)
					{
						Set(upDown, nameof(UpDownBase.BorderStyle), upDown.BorderStyle, BorderStyle.FixedSingle, v => upDown.BorderStyle = v);
					}
					break;
				case ProgressBar progressBar:
					SetBackColor(progressBar, colors.Surface);
					SetForeColor(progressBar, colors.Selection);
					break;
				case DataGridView dataGridView:
					ApplyDark(dataGridView, colors);
					break;
				case ToolStrip toolStrip:
					SetForeColor(toolStrip, colors.Text);
					// Status strips use the (light) system renderer by default.
					if (toolStrip.RenderMode == ToolStripRenderMode.System)
					{
						Set(toolStrip, nameof(ToolStrip.RenderMode), toolStrip.RenderMode, ToolStripRenderMode.ManagerRenderMode, v => toolStrip.RenderMode = v);
					}
					break;
			}
		}

		private static void ApplyDark(DataGridView dataGridView, ThemeColors colors)
		{
			Set(dataGridView, nameof(DataGridView.BackgroundColor), dataGridView.BackgroundColor, colors.Surface, v => dataGridView.BackgroundColor = v);
			Set(dataGridView, nameof(DataGridView.GridColor), dataGridView.GridColor, colors.GridLine, v => dataGridView.GridColor = v);
			Set(dataGridView, nameof(DataGridView.EnableHeadersVisualStyles), dataGridView.EnableHeadersVisualStyles, false, v => dataGridView.EnableHeadersVisualStyles = v);
			// The default raised header borders are drawn with light 3D colors.
			if (dataGridView.ColumnHeadersBorderStyle == DataGridViewHeaderBorderStyle.Raised)
			{
				Set(dataGridView, nameof(DataGridView.ColumnHeadersBorderStyle), dataGridView.ColumnHeadersBorderStyle, DataGridViewHeaderBorderStyle.Single, v => dataGridView.ColumnHeadersBorderStyle = v);
			}
			if (dataGridView.RowHeadersBorderStyle == DataGridViewHeaderBorderStyle.Raised)
			{
				Set(dataGridView, nameof(DataGridView.RowHeadersBorderStyle), dataGridView.RowHeadersBorderStyle, DataGridViewHeaderBorderStyle.Single, v => dataGridView.RowHeadersBorderStyle = v);
			}
			if (dataGridView.BorderStyle == BorderStyle.Fixed3D)
			{
				Set(dataGridView, nameof(DataGridView.BorderStyle), dataGridView.BorderStyle, BorderStyle.FixedSingle, v => dataGridView.BorderStyle = v);
			}

			SetCellStyle(dataGridView, nameof(DataGridView.DefaultCellStyle), dataGridView.DefaultCellStyle, colors.Surface, colors.Text, colors.Selection, colors.SelectionText);
			SetCellStyle(dataGridView, nameof(DataGridView.ColumnHeadersDefaultCellStyle), dataGridView.ColumnHeadersDefaultCellStyle, colors.Header, colors.Text, colors.Header, colors.Text);
			SetCellStyle(dataGridView, nameof(DataGridView.RowHeadersDefaultCellStyle), dataGridView.RowHeadersDefaultCellStyle, colors.Header, colors.Text, colors.Selection, colors.SelectionText);
		}

		private static void SetCellStyle(DataGridView owner, string name, DataGridViewCellStyle style, Color back, Color fore, Color selectionBack, Color selectionFore)
		{
			Set(owner, name + ".BackColor", style.BackColor, back, v => style.BackColor = v);
			Set(owner, name + ".ForeColor", style.ForeColor, fore, v => style.ForeColor = v);
			Set(owner, name + ".SelectionBackColor", style.SelectionBackColor, selectionBack, v => style.SelectionBackColor = v);
			Set(owner, name + ".SelectionForeColor", style.SelectionForeColor, selectionFore, v => style.SelectionForeColor = v);
		}

		private static void SetBorderStyle(TextBox textBox)
		{
			// The themed 3D border is drawn in a light color.
			if (textBox.BorderStyle == BorderStyle.Fixed3D)
			{
				Set(textBox, nameof(TextBox.BorderStyle), textBox.BorderStyle, BorderStyle.FixedSingle, v => textBox.BorderStyle = v);
			}
		}

		private static void RestoreLight(Control control)
		{
			if (control is TabControl tabControl && tabPainters.TryGetValue(tabControl, out var painter))
			{
				tabPainters.Remove(tabControl);
				painter.Dispose();
			}

			if (restoreActions.TryGetValue(control, out var actions))
			{
				restoreActions.Remove(control);

				// Restore in reverse order, so dependent properties are reset correctly.
				foreach (var action in actions.Values.Reverse())
				{
					action();
				}
			}
		}

		/// <summary>Applies the native (uxtheme) dark mode to controls which support it.</summary>
		private static void ApplyNativeTheme(Control control)
		{
			if (!control.IsHandleCreated || !DarkModeNative.IsSupported)
			{
				return;
			}

			var isThemed = nativeThemedControls.TryGetValue(control, out _);
			if (!IsDark && !isThemed)
			{
				// Never touch controls in light mode which were never themed.
				return;
			}
			if (IsDark && !isThemed)
			{
				nativeThemedControls.Add(control, marker);
			}

			var dark = IsDark;

			foreach (var toolTip in GetToolTips(control))
			{
				ApplyNativeTheme(toolTip);
			}

			switch (control)
			{
				case Form form:
					DarkModeNative.SetTitleBarDarkMode(form.Handle, dark);
					break;
				case ButtonBase buttonBase when buttonBase.FlatStyle == FlatStyle.System:
					DarkModeNative.SetTheme(buttonBase.Handle, dark ? "Explorer" : null, dark);
					break;
				case ComboBox comboBox:
					DarkModeNative.SetTheme(comboBox.Handle, dark ? "DarkMode_CFD" : null, dark);
					DarkModeNative.SetTheme(DarkModeNative.GetComboBoxListHandle(comboBox.Handle), dark ? "DarkMode_Explorer" : null, dark);
					break;
				case ProgressBar progressBar:
					if (dark)
					{
						// Without visual styles the bar uses the BackColor and ForeColor.
						DarkModeNative.DisableTheme(progressBar.Handle);
					}
					else
					{
						DarkModeNative.SetTheme(progressBar.Handle, null, false);
					}
					break;
				case DataGridView dataGridView:
					foreach (var scrollBar in GetDataGridViewScrollBars(dataGridView))
					{
						HookControl(scrollBar);
						ApplyNativeTheme(scrollBar);
					}
					break;
				case TextBoxBase _:
				case ListBox _:
				case TreeView _:
				case ListView _:
				case ScrollBar _:
					DarkModeNative.SetTheme(control.Handle, dark ? "DarkMode_Explorer" : null, dark);
					break;
				case ScrollableControl _ when !(control is ToolStrip) && !(control is UpDownBase):
					// Panels and user controls with scroll bars.
					DarkModeNative.SetTheme(control.Handle, dark ? "DarkMode_Explorer" : null, dark);
					break;
			}
		}

		private static readonly PropertyInfo toolTipHandleProperty = typeof(ToolTip).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly PropertyInfo toolStripToolTipProperty = typeof(ToolStrip).GetProperty("ToolTip", BindingFlags.Instance | BindingFlags.NonPublic);

		private static void ApplyNativeTheme(ToolTip toolTip)
		{
			var isThemed = nativeThemedToolTips.TryGetValue(toolTip, out _);
			if (!IsDark && !isThemed)
			{
				return;
			}
			if (IsDark && !isThemed)
			{
				nativeThemedToolTips.Add(toolTip, marker);
			}

			try
			{
				if (toolTipHandleProperty?.GetValue(toolTip) is IntPtr handle)
				{
					DarkModeNative.SetTheme(handle, IsDark ? "DarkMode_Explorer" : null, IsDark);
				}
			}
			catch
			{
				// ignored
			}
		}

		/// <summary>
		/// Gets the tool tips owned by the control: tool tip components of forms and user controls and the item tool tip of tool strips.
		/// </summary>
		private static IEnumerable<ToolTip> GetToolTips(Control control)
		{
			if (control is ToolStrip toolStrip)
			{
				if (toolStrip.ShowItemToolTips && toolStripToolTipProperty?.GetValue(toolStrip) is ToolTip toolStripToolTip)
				{
					yield return toolStripToolTip;
				}
			}
			else if (control is ContainerControl)
			{
				for (var type = control.GetType(); type != null && type != typeof(ContainerControl); type = type.BaseType)
				{
					foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
					{
						if (typeof(ToolTip).IsAssignableFrom(field.FieldType) && field.GetValue(control) is ToolTip toolTip)
						{
							yield return toolTip;
						}
					}
				}
			}
		}

		private static IEnumerable<ScrollBar> GetDataGridViewScrollBars(DataGridView dataGridView)
		{
			foreach (var name in new[] { "HorizontalScrollBar", "VerticalScrollBar" })
			{
				var property = typeof(DataGridView).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic);
				if (property?.GetValue(dataGridView) is ScrollBar scrollBar)
				{
					yield return scrollBar;
				}
			}
		}

		#endregion

		#region Property Helpers

		/// <summary>Sets a property and records how to restore its original value.</summary>
		private static void Set<T>(object target, string key, T currentValue, T newValue, Action<T> setter)
		{
			var actions = restoreActions.GetOrCreateValue(target);
			if (!actions.ContainsKey(key))
			{
				actions.Add(key, () => setter(currentValue));
			}

			setter(newValue);
		}

		private static void SetBackColor(Control control, Color color)
		{
			if (!ShouldThemeColor(control, nameof(Control.BackColor), control.BackColor))
			{
				return;
			}

			Set(control, nameof(Control.BackColor), GetExplicitColor(control, nameof(Control.BackColor), control.BackColor), color, v => control.BackColor = v);
		}

		private static void SetForeColor(Control control, Color color)
		{
			if (!ShouldThemeColor(control, nameof(Control.ForeColor), control.ForeColor))
			{
				return;
			}

			Set(control, nameof(Control.ForeColor), GetExplicitColor(control, nameof(Control.ForeColor), control.ForeColor), color, v => control.ForeColor = v);
		}

		/// <summary>
		/// Colors explicitly set to a non-system color (e.g. the color preview panels) are intentional and stay untouched.
		/// </summary>
		private static bool ShouldThemeColor(Control control, string propertyName, Color current)
		{
			if (restoreActions.TryGetValue(control, out var actions) && actions.ContainsKey(propertyName))
			{
				return true;
			}

			return !IsExplicitlySet(control, propertyName) || current.IsSystemColor;
		}

		/// <summary>Gets the value to restore: <see cref="Color.Empty"/> resets an ambient color to its default behaviour.</summary>
		private static Color GetExplicitColor(Control control, string propertyName, Color current)
		{
			return IsExplicitlySet(control, propertyName) ? current : Color.Empty;
		}

		private static bool IsExplicitlySet(Control control, string propertyName)
		{
			return TypeDescriptor.GetProperties(control)[propertyName]?.ShouldSerializeValue(control) ?? false;
		}

		#endregion

		#region Custom Painting

		private static bool IsUpDownButtons(Control control) => control.GetType().Name == "UpDownButtons";

		/// <summary>Replaces the light border of the group box.</summary>
		private static void OnGroupBoxPaint(object sender, PaintEventArgs e)
		{
			if (!IsDark)
			{
				return;
			}

			var groupBox = (GroupBox)sender;
			var colors = Colors;
			var g = e.Graphics;

			using (var brush = new SolidBrush(groupBox.BackColor))
			{
				g.FillRectangle(brush, groupBox.ClientRectangle);
			}

			const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

			var textSize = string.IsNullOrEmpty(groupBox.Text) ? Size.Empty : TextRenderer.MeasureText(g, groupBox.Text, groupBox.Font, Size.Empty, flags);
			var lineY = Math.Max(textSize.Height / 2, 0);

			using (var pen = new Pen(colors.Border))
			{
				g.DrawRectangle(pen, 0, lineY, groupBox.Width - 1, groupBox.Height - lineY - 1);
			}

			if (textSize != Size.Empty)
			{
				var textRect = new Rectangle(6, 0, textSize.Width + 4, textSize.Height);
				using (var brush = new SolidBrush(groupBox.BackColor))
				{
					g.FillRectangle(brush, textRect);
				}
				textRect.X += 2;
				TextRenderer.DrawText(g, groupBox.Text, groupBox.Font, textRect, groupBox.Enabled ? colors.Text : colors.DisabledText, flags);
			}
		}

		/// <summary>Paints the corner between both scroll bars which is always drawn in the light control color.</summary>
		private static void OnDataGridViewPaint(object sender, PaintEventArgs e)
		{
			if (!IsDark)
			{
				return;
			}

			var dataGridView = (DataGridView)sender;
			var scrollBars = GetDataGridViewScrollBars(dataGridView).ToList();
			var horizontal = scrollBars.FirstOrDefault(s => s is HScrollBar);
			var vertical = scrollBars.FirstOrDefault(s => s is VScrollBar);
			if (horizontal == null || vertical == null || !horizontal.Visible || !vertical.Visible)
			{
				return;
			}

			using var brush = new SolidBrush(Colors.Surface);
			e.Graphics.FillRectangle(brush, vertical.Left, horizontal.Top, vertical.Width, horizontal.Height);
		}

		/// <summary>Repaints the spin buttons of a <see cref="NumericUpDown"/> which are drawn by WinForms in light colors.</summary>
		private static void OnUpDownButtonsPaint(object sender, PaintEventArgs e)
		{
			if (!IsDark)
			{
				return;
			}

			var control = (Control)sender;
			var colors = Colors;
			var g = e.Graphics;

			var half = control.Height / 2;
			var upper = new Rectangle(0, 0, control.Width, half);
			var lower = new Rectangle(0, half, control.Width, control.Height - half);

			var mouse = control.PointToClient(Control.MousePosition);
			var isPressed = (Control.MouseButtons & MouseButtons.Left) != 0;

			foreach (var (rect, isUp) in new[] { (upper, true), (lower, false) })
			{
				var isHot = control.Enabled && rect.Contains(mouse);
				var back = isHot ? (isPressed ? colors.Pressed : colors.Hover) : colors.Header;
				using (var brush = new SolidBrush(back))
				{
					g.FillRectangle(brush, rect);
				}

				// Draw the arrow line by line to keep it crisp and symmetric.
				var cx = rect.Left + (rect.Width - 1) / 2;
				var arrowHeight = Math.Max(2, Math.Min(rect.Width / 3, rect.Height / 2 - 1));
				var top = rect.Top + (rect.Height - arrowHeight) / 2;
				using (var brush = new SolidBrush(control.Enabled ? colors.Text : colors.DisabledText))
				{
					for (var i = 0; i < arrowHeight; ++i)
					{
						var y = isUp ? top + i : top + arrowHeight - 1 - i;
						g.FillRectangle(brush, cx - i, y, i * 2 + 1, 1);
					}
				}
			}

			using var pen = new Pen(colors.Border);
			g.DrawLine(pen, 0, 0, 0, control.Height);
			g.DrawLine(pen, 0, half, control.Width, half);
		}

		private static readonly PropertyInfo showKeyboardCuesProperty = typeof(Control).GetProperty("ShowKeyboardCues", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly PropertyInfo showFocusCuesProperty = typeof(Control).GetProperty("ShowFocusCues", BindingFlags.Instance | BindingFlags.NonPublic);

		private static bool GetProtectedBool(Control control, string name)
		{
			var property = name == "ShowKeyboardCues" ? showKeyboardCuesProperty : showFocusCuesProperty;
			return property?.GetValue(control) as bool? ?? true;
		}

		private static bool IsOwnerDrawableRadioButton(RadioButton radioButton)
		{
			return radioButton.FlatStyle == FlatStyle.Standard
				&& radioButton.Appearance == Appearance.Normal
				&& radioButton.CheckAlign == System.Drawing.ContentAlignment.MiddleLeft
				&& radioButton.Image == null
				&& radioButton.RightToLeft != RightToLeft.Yes;
		}

		/// <summary>Paints a radio button with a dark glyph. The themed glyph is white and the native dark mode draws black text.</summary>
		private static void OnRadioButtonPaint(object sender, PaintEventArgs e)
		{
			var radioButton = (RadioButton)sender;
			if (!IsDark || !IsOwnerDrawableRadioButton(radioButton))
			{
				return;
			}

			var colors = Colors;
			var g = e.Graphics;
			var bounds = radioButton.ClientRectangle;

			using (var brush = new SolidBrush(radioButton.BackColor))
			{
				g.FillRectangle(brush, bounds);
			}

			var glyphSize = RadioButtonRenderer.GetGlyphSize(g, RadioButtonState.UncheckedNormal);
			var glyph = new Rectangle(bounds.Left + radioButton.Padding.Left, bounds.Top + (bounds.Height - glyphSize.Height) / 2, glyphSize.Width - 1, glyphSize.Height - 1);

			var isHot = radioButton.Enabled && bounds.Contains(radioButton.PointToClient(Control.MousePosition));
			var accent = Color.FromArgb(96, 205, 255);

			var smoothingMode = g.SmoothingMode;
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			if (radioButton.Checked)
			{
				using (var brush = new SolidBrush(radioButton.Enabled ? accent : colors.DisabledText))
				{
					g.FillEllipse(brush, glyph);
				}
				var dot = glyph;
				dot.Inflate(-glyph.Width / 4, -glyph.Height / 4);
				using (var brush = new SolidBrush(colors.Window))
				{
					g.FillEllipse(brush, dot);
				}
			}
			else
			{
				using (var brush = new SolidBrush(isHot ? colors.Hover : colors.Surface))
				{
					g.FillEllipse(brush, glyph);
				}
				using (var pen = new Pen(radioButton.Enabled ? Color.FromArgb(160, 160, 160) : colors.DisabledText))
				{
					g.DrawEllipse(pen, glyph);
				}
			}
			g.SmoothingMode = smoothingMode;

			var textBounds = new Rectangle(glyph.Right + 4, bounds.Top, Math.Max(0, bounds.Right - glyph.Right - 4), bounds.Height);
			var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
			if (!radioButton.UseMnemonic)
			{
				flags |= TextFormatFlags.NoPrefix;
			}
			else if (!GetProtectedBool(radioButton, "ShowKeyboardCues"))
			{
				flags |= TextFormatFlags.HidePrefix;
			}
			TextRenderer.DrawText(g, radioButton.Text, radioButton.Font, textBounds, radioButton.Enabled ? colors.Text : colors.DisabledText, flags);

			if (radioButton.Focused && GetProtectedBool(radioButton, "ShowFocusCues"))
			{
				var textSize = TextRenderer.MeasureText(g, radioButton.Text, radioButton.Font, textBounds.Size, flags);
				var focusBounds = new Rectangle(textBounds.Left - 1, textBounds.Top + (textBounds.Height - textSize.Height) / 2, Math.Min(textSize.Width + 2, textBounds.Width), textSize.Height);
				ControlPaint.DrawFocusRectangle(g, focusBounds, colors.Text, radioButton.BackColor);
			}
		}

		/// <summary>Draws the single border of an up-down control in the theme color.</summary>
		private static void OnUpDownBasePaint(object sender, PaintEventArgs e)
		{
			var upDown = (UpDownBase)sender;
			if (!IsDark || upDown.BorderStyle != BorderStyle.FixedSingle)
			{
				return;
			}

			using var pen = new Pen(Colors.Border);
			e.Graphics.DrawRectangle(pen, 0, 0, upDown.Width - 1, upDown.Height - 1);
		}

		#endregion
	}
}
