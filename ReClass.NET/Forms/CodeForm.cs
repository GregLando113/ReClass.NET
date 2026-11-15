using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using ReClassNET.CodeGenerator;
using ReClassNET.Extensions;
using ReClassNET.Logger;
using ReClassNET.Nodes;
using ReClassNET.Project;
using ReClassNET.UI;
using ReClassNET.UI.Theming;
using ReClassNET.Util.Rtf;

namespace ReClassNET.Forms
{
	public partial class CodeForm : IconForm
	{
		private readonly string code;
		private readonly ILanguage language;

		public CodeForm(ICodeGenerator generator, IReadOnlyList<ClassNode> classes, IReadOnlyList<EnumDescription> enums, ILogger logger)
		{
			Contract.Requires(generator != null);
			Contract.Requires(classes != null);
			Contract.Requires(enums != null);

			InitializeComponent();

			codeRichTextBox.SetInnerMargin(5, 5, 5, 5);

			code = generator.GenerateCode(classes, enums, logger);
			language = generator.Language == Language.Cpp ? Languages.Cpp : Languages.CSharp;

			ThemeManager.ThemeChanged += OnThemeChanged;
		}

		private void OnThemeChanged(object sender, EventArgs e) => UpdateCode();

		private void UpdateCode()
		{
			var buffer = new StringBuilder(code.Length * 2);
			using (var writer = new StringWriter(buffer))
			{
				new CodeColorizer().Colorize(
					code,
					language,
					new RtfFormatter(ThemeManager.IsDark),
					StyleSheets.Default,
					writer
				);
			}

			codeRichTextBox.Rtf = buffer.ToString();
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			GlobalWindowManager.AddWindow(this);

			// Set the text after the window got themed, because changing the ForeColor recolors the whole text.
			UpdateCode();
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			base.OnFormClosed(e);

			ThemeManager.ThemeChanged -= OnThemeChanged;

			GlobalWindowManager.RemoveWindow(this);
		}
	}

	internal class RtfFormatter : IFormatter
	{
		private readonly RtfBuilder builder = new RtfBuilder(RtfFont.Consolas, 20);
		private readonly bool useDarkColors;

		public RtfFormatter(bool useDarkColors)
		{
			this.useDarkColors = useDarkColors;
		}

		public void Write(string parsedSourceCode, IList<Scope> scopes, IStyleSheet styleSheet, TextWriter textWriter)
		{
			if (useDarkColors)
			{
				var scopeName = scopes.FirstOrDefault()?.Name;
				var lightColor = scopeName != null && styleSheet.Styles.Contains(scopeName) ? styleSheet.Styles[scopeName].Foreground : Color.Empty;

				builder.SetForeColor(DarkCodeColors.Get(scopeName, lightColor)).Append(parsedSourceCode);
			}
			else if (scopes.Any())
			{
				builder.SetForeColor(styleSheet.Styles[scopes.First().Name].Foreground).Append(parsedSourceCode);
			}
			else
			{
				builder.Append(parsedSourceCode);
			}
		}

		public void WriteHeader(IStyleSheet styleSheet, ILanguage language, TextWriter textWriter)
		{

		}

		public void WriteFooter(IStyleSheet styleSheet, ILanguage language, TextWriter textWriter)
		{
			textWriter.Write(builder.ToString());
		}
	}

	/// <summary>Syntax highlighting colors for a dark background.</summary>
	internal static class DarkCodeColors
	{
		private static readonly Color defaultColor = Color.FromArgb(220, 220, 220);

		private static readonly Dictionary<string, Color> colors = new Dictionary<string, Color>
		{
			[ScopeName.PlainText] = defaultColor,
			[ScopeName.Comment] = Color.FromArgb(87, 166, 74),
			[ScopeName.XmlDocComment] = Color.FromArgb(87, 166, 74),
			[ScopeName.XmlDocTag] = Color.FromArgb(128, 128, 128),
			[ScopeName.String] = Color.FromArgb(214, 157, 133),
			[ScopeName.StringCSharpVerbatim] = Color.FromArgb(214, 157, 133),
			[ScopeName.StringEscape] = Color.FromArgb(255, 214, 143),
			[ScopeName.Keyword] = Color.FromArgb(86, 156, 214),
			[ScopeName.ControlKeyword] = Color.FromArgb(216, 160, 223),
			[ScopeName.PreprocessorKeyword] = Color.FromArgb(155, 155, 155),
			[ScopeName.PseudoKeyword] = Color.FromArgb(86, 156, 214),
			[ScopeName.Predefined] = Color.FromArgb(86, 156, 214),
			[ScopeName.ClassName] = Color.FromArgb(78, 201, 176),
			[ScopeName.Type] = Color.FromArgb(78, 201, 176),
			[ScopeName.TypeVariable] = Color.FromArgb(78, 201, 176),
			[ScopeName.NameSpace] = Color.FromArgb(200, 200, 200),
			[ScopeName.Constructor] = Color.FromArgb(220, 220, 170),
			[ScopeName.Number] = Color.FromArgb(181, 206, 168)
		};

		public static Color Get(string scopeName, Color lightColor)
		{
			if (scopeName != null && colors.TryGetValue(scopeName, out var color))
			{
				return color;
			}

			return lightColor.IsEmpty || lightColor.ToArgb() == Color.Black.ToArgb() ? defaultColor : Lighten(lightColor);
		}

		/// <summary>Keeps the hue of a color but makes it readable on a dark background.</summary>
		private static Color Lighten(Color color)
		{
			var hue = color.GetHue() / 360.0;
			var saturation = color.GetSaturation();
			var lightness = Math.Max(color.GetBrightness(), 0.7);

			double HueToRgb(double p, double q, double t)
			{
				if (t < 0) t += 1;
				if (t > 1) t -= 1;
				if (t < 1 / 6.0) return p + (q - p) * 6 * t;
				if (t < 1 / 2.0) return q;
				if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
				return p;
			}

			var q2 = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
			var p2 = 2 * lightness - q2;

			return Color.FromArgb(
				(int)Math.Round(HueToRgb(p2, q2, hue + 1 / 3.0) * 255),
				(int)Math.Round(HueToRgb(p2, q2, hue) * 255),
				(int)Math.Round(HueToRgb(p2, q2, hue - 1 / 3.0) * 255)
			);
		}
	}
}
