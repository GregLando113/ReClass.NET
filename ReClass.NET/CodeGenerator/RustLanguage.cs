using System.Collections.Generic;
using ColorCode;
using ColorCode.Common;

namespace ReClassNET.CodeGenerator
{
	/// <summary>
	/// The Rust syntax for the highlighting of the generated code. ColorCode doesn't provide it.
	/// </summary>
	internal class RustLanguage : ILanguage
	{
		public static RustLanguage Instance { get; } = new RustLanguage();

		static RustLanguage()
		{
			// The parser only compiles languages it can find by their id.
			Languages.Load(Instance);
		}

		private RustLanguage()
		{
		}

		public string Id => "rust";

		public string Name => "Rust";

		public string CssClassName => "rust";

		public string FirstLinePattern => null;

		public IList<LanguageRule> Rules { get; } = new List<LanguageRule>
		{
			new LanguageRule(
				@"/\*([^*]|[\r\n]|(\*+([^*/]|[\r\n])))*\*+/",
				new Dictionary<int, string> { { 0, ScopeName.Comment } }),
			new LanguageRule(
				@"(//.*?)\r?$",
				new Dictionary<int, string> { { 1, ScopeName.Comment } }),
			new LanguageRule(
				@"(?s)""(?:[^""\\]|\\.)*""",
				new Dictionary<int, string> { { 0, ScopeName.String } }),
			new LanguageRule(
				@"#!?\[[^\]\r\n]*\]",
				new Dictionary<int, string> { { 0, ScopeName.PreprocessorKeyword } }),
			new LanguageRule(
				@"\b(as|async|await|break|const|continue|crate|dyn|else|enum|extern|false|fn|for|if|impl|in|let|loop|match|mod|move|mut|pub|ref|return|self|Self|static|struct|super|trait|true|type|union|unsafe|use|where|while)\b",
				new Dictionary<int, string> { { 1, ScopeName.Keyword } }),
			new LanguageRule(
				@"\b(bool|char|str|f32|f64|i8|i16|i32|i64|i128|isize|u8|u16|u32|u64|u128|usize)\b",
				new Dictionary<int, string> { { 1, ScopeName.Keyword } }),
			new LanguageRule(
				@"\b(0x[0-9a-fA-F_]+|[0-9][0-9_]*(\.[0-9_]+)?)\b",
				new Dictionary<int, string> { { 1, ScopeName.Number } })
		};

		public bool HasAlias(string lang)
		{
			switch (lang.ToLower())
			{
				case "rs":
				case "rust":
					return true;
				default:
					return false;
			}
		}

		public override string ToString()
		{
			return Name;
		}
	}
}
