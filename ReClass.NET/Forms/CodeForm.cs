using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using ColorCode.Styling;
using ReClassNET.CodeGenerator;
using ReClassNET.Extensions;
using ReClassNET.Logger;
using ReClassNET.Nodes;
using ReClassNET.Project;
using ReClassNET.UI;
using ReClassNET.Util.Rtf;

namespace ReClassNET.Forms
{
	public partial class CodeForm : IconForm
	{
		public CodeForm(ICodeGenerator generator, IReadOnlyList<ClassNode> classes, IReadOnlyList<EnumDescription> enums, ILogger logger)
		{
			Contract.Requires(generator != null);
			Contract.Requires(classes != null);
			Contract.Requires(enums != null);

			InitializeComponent();

			codeRichTextBox.SetInnerMargin(5, 5, 5, 5);

			var code = generator.GenerateCode(classes, enums, logger);

			codeRichTextBox.Rtf = new RtfCodeColorizer().Colorize(
				code,
				generator.Language == Language.Cpp ? Languages.Cpp : Languages.CSharp
			);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			GlobalWindowManager.AddWindow(this);
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			base.OnFormClosed(e);

			GlobalWindowManager.RemoveWindow(this);
		}
	}

	/// <summary>Colorizes source code and returns it as RTF.</summary>
	internal class RtfCodeColorizer : CodeColorizerBase
	{
		private RtfBuilder builder;

		public RtfCodeColorizer()
			: base(Application.IsDarkModeEnabled ? StyleDictionary.DefaultDark : StyleDictionary.DefaultLight, null)
		{
		}

		public string Colorize(string sourceCode, ILanguage language)
		{
			builder = new RtfBuilder(RtfFont.Consolas, 20);

			// The parser may split a line break into separate chunks which the RtfBuilder wouldn't detect anymore.
			// So use a single character while parsing and restore the line breaks when writing.
			languageParser.Parse(sourceCode.Replace("\r\n", "\n"), language, Write);

			return builder.ToString();
		}

		protected override void Write(string parsedSourceCode, IList<Scope> scopes)
		{
			// Text without a scope must use the plain text color because the default (black) is unreadable on the dark theme.
			if (TryGetForeColor(scopes.Count > 0 ? scopes[0].Name : ScopeName.PlainText, out var color)
				|| TryGetForeColor(ScopeName.PlainText, out color))
			{
				builder.SetForeColor(color);
			}

			builder.Append(parsedSourceCode.Replace("\n", Environment.NewLine));
		}

		private bool TryGetForeColor(string scopeName, out Color color)
		{
			color = default;

			if (!Styles.Contains(scopeName))
			{
				return false;
			}

			// The colors are stored as #AARRGGBB.
			var foreground = Styles[scopeName].Foreground;
			if (string.IsNullOrEmpty(foreground) || !int.TryParse(foreground.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
			{
				return false;
			}

			color = Color.FromArgb(argb);

			return true;
		}
	}
}
