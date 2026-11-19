using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using ReClassNET.Extensions;
using ReClassNET.Logger;
using ReClassNET.Nodes;
using ReClassNET.Project;

namespace ReClassNET.CodeGenerator
{
	/// <summary>
	/// A Rust code generator. Classes are written as <c>#[repr(C)]</c> structs with explicit padding, templates as generic structs.
	/// </summary>
	public class RustCodeGenerator : ICodeGenerator
	{
		private const string VoidType = "core::ffi::c_void";

		private static readonly Dictionary<Type, string> nodeTypeToTypeDefinationMap = new Dictionary<Type, string>
		{
			[typeof(BoolNode)] = "bool",
			[typeof(DoubleNode)] = "f64",
			[typeof(FloatNode)] = "f32",
			[typeof(FunctionPtrNode)] = "*const " + VoidType,
			[typeof(Int8Node)] = "i8",
			[typeof(Int16Node)] = "i16",
			[typeof(Int32Node)] = "i32",
			[typeof(Int64Node)] = "i64",
			[typeof(NIntNode)] = "isize",
			[typeof(Matrix3x3Node)] = "[[f32; 3]; 3]",
			[typeof(Matrix3x4Node)] = "[[f32; 4]; 3]",
			[typeof(Matrix4x4Node)] = "[[f32; 4]; 4]",
			[typeof(UInt8Node)] = "u8",
			[typeof(UInt16Node)] = "u16",
			[typeof(UInt32Node)] = "u32",
			[typeof(UInt64Node)] = "u64",
			[typeof(NUIntNode)] = "usize",
			[typeof(Vector2Node)] = "[f32; 2]",
			[typeof(Vector3Node)] = "[f32; 3]",
			[typeof(Vector4Node)] = "[f32; 4]"
		};

		private static readonly HashSet<string> keywords = new HashSet<string>
		{
			"as", "break", "const", "continue", "crate", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop",
			"match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super", "trait", "true", "type",
			"unsafe", "use", "where", "while", "async", "await", "dyn", "abstract", "become", "box", "do", "final", "gen", "macro",
			"override", "priv", "try", "typeof", "unsized", "virtual", "yield"
		};

		/// <summary>The keywords which can't be used as raw identifier (r#name).</summary>
		private static readonly HashSet<string> keywordsWithoutRawIdentifier = new HashSet<string> { "crate", "self", "Self", "super" };

		/// <summary>A struct or union which is written for a class.</summary>
		private class Item
		{
			public string Keyword;
			public string Name;
			public IReadOnlyList<string> Parameters;
			public List<string> Fields;
		}

		/// <summary>The state while a class is written. Unions and virtual method tables of the class become separate items.</summary>
		private class ClassContext
		{
			public string ClassName;
			public ILogger Logger;
			public readonly List<Item> Items = new List<Item>();
			public readonly HashSet<string> TypeNames = new HashSet<string>();
		}

		public Language Language => Language.Rust;

		public string GenerateCode(IReadOnlyList<ClassNode> classes, IReadOnlyList<EnumDescription> enums, ILogger logger)
		{
			using var sw = new StringWriter();
			using var iw = new IndentedTextWriter(sw, "    ");

			iw.WriteLine($"// Created with {Constants.ApplicationName} {Constants.ApplicationVersion} by {Constants.Author}");

			foreach (var @enum in enums)
			{
				iw.WriteLine();

				WriteEnum(iw, @enum);
			}

			// Rust doesn't care about the order but the classes should look the same in every language.
			foreach (var @class in CodeGeneratorUtil.OrderClassesByDependencies(classes))
			{
				iw.WriteLine();

				WriteClass(iw, @class, logger);
			}

			return sw.ToString();
		}

		/// <summary>
		/// Outputs the Rust code for the given enum to the <see cref="TextWriter"/> instance.
		/// </summary>
		/// <param name="writer">The writer to output to.</param>
		/// <param name="enum">The enum to output.</param>
		private static void WriteEnum(IndentedTextWriter writer, EnumDescription @enum)
		{
			Contract.Requires(writer != null);
			Contract.Requires(@enum != null);

			var name = GetIdentifier(@enum.Name, "Enum");
			var size = (int)@enum.Size;

			// The values of a flags enum use the full unsigned range.
			string FormatValue(long value)
			{
				if (!@enum.UseFlagsMode)
				{
					return value.ToString();
				}

				return size switch
				{
					1 => ((byte)value).ToString(),
					2 => ((ushort)value).ToString(),
					4 => ((uint)value).ToString(),
					_ => ((ulong)value).ToString()
				};
			}

			// Rust doesn't allow two variants with the same value, so further names for a value become constants.
			var variants = new List<KeyValuePair<string, long>>();
			var aliases = new List<KeyValuePair<string, string>>();
			var usedNames = new HashSet<string>();
			foreach (var kv in @enum.Values)
			{
				var itemName = GetUniqueName(GetIdentifier(kv.Key, "Item"), usedNames);

				var index = variants.FindIndex(v => v.Value == kv.Value);
				if (index == -1)
				{
					variants.Add(new KeyValuePair<string, long>(itemName, kv.Value));
				}
				else
				{
					aliases.Add(new KeyValuePair<string, string>(itemName, variants[index].Key));
				}
			}

			writer.WriteLine("#[allow(non_camel_case_types)]");
			if (variants.Count > 0)
			{
				// An enum without variants can't have a representation.
				writer.WriteLine($"#[repr({(@enum.UseFlagsMode ? "u" : "i")}{size * 8})]");
			}
			writer.WriteLine("#[derive(Clone, Copy, PartialEq, Eq, Debug)]");
			writer.WriteLine($"pub enum {name} {{");
			writer.Indent++;
			foreach (var kv in variants)
			{
				writer.WriteLine($"{kv.Key} = {FormatValue(kv.Value)},");
			}
			writer.Indent--;
			writer.WriteLine("}");

			if (aliases.Count > 0)
			{
				writer.WriteLine();
				writer.WriteLine("#[allow(non_upper_case_globals)]");
				writer.WriteLine($"impl {name} {{");
				writer.Indent++;
				foreach (var kv in aliases)
				{
					writer.WriteLine($"pub const {kv.Key}: Self = Self::{kv.Value};");
				}
				writer.Indent--;
				writer.WriteLine("}");
			}
		}

		/// <summary>
		/// Outputs the Rust code for the given class to the <see cref="TextWriter"/> instance.
		/// </summary>
		/// <param name="writer">The writer to output to.</param>
		/// <param name="class">The class to output.</param>
		/// <param name="logger">The logger.</param>
		private static void WriteClass(IndentedTextWriter writer, ClassNode @class, ILogger logger)
		{
			Contract.Requires(writer != null);
			Contract.Requires(@class != null);

			var context = new ClassContext
			{
				ClassName = SanitizeName(@class.Name, "Class"),
				Logger = logger
			};

			var name = EscapeKeyword(context.ClassName);
			context.TypeNames.Add(name);

			var templateNode = @class as TemplateNode;

			var fields = CreateFields(@class.Nodes.WhereNot(n => n is FunctionNode), true, context);

			// The unions and virtual method tables which were found while the fields were created.
			foreach (var item in context.Items)
			{
				WriteItem(writer, item);

				writer.WriteLine();
			}

			if (!string.IsNullOrEmpty(@class.Comment))
			{
				writer.WriteLine($"// {ToSingleLine(@class.Comment)}");
			}

			WriteItem(
				writer,
				new Item
				{
					Keyword = "struct",
					Name = name,
					Parameters = templateNode?.Parameters.Select(EscapeKeyword).ToList(),
					Fields = fields
				},
				false
			);

			if (templateNode != null)
			{
				// The size of a template depends on its arguments.
				writer.WriteLine();

				return;
			}

			writer.WriteLine($" // Size: 0x{@class.MemorySize:X04}");
			writer.WriteLine($"const _: () = assert!(core::mem::size_of::<{name}>() == 0x{@class.MemorySize:X});");
		}

		/// <summary>
		/// Outputs the Rust code for the struct or union to the <see cref="TextWriter"/> instance.
		/// </summary>
		private static void WriteItem(IndentedTextWriter writer, Item item, bool endLine = true)
		{
			writer.WriteLine("#[allow(non_camel_case_types, non_snake_case)]");
			writer.WriteLine("#[repr(C)]");
			writer.WriteLine("#[derive(Clone, Copy)]");
			writer.Write($"pub {item.Keyword} {item.Name}");
			if (item.Parameters != null && item.Parameters.Count > 0)
			{
				// The bound is needed because the item is Copy, which is what allows it to be a member of a union.
				writer.Write($"<{string.Join(", ", item.Parameters.Select(p => p + ": Copy"))}>");
			}
			writer.WriteLine(" {");
			writer.Indent++;
			foreach (var field in item.Fields)
			{
				writer.WriteLine(field);
			}
			writer.Indent--;
			writer.Write("}");
			if (endLine)
			{
				writer.WriteLine();
			}
		}

		/// <summary>
		/// Creates the field declarations for the given nodes.
		/// </summary>
		/// <param name="nodes">The nodes to output.</param>
		/// <param name="mergePadding">True to merge consecutive hex nodes into one padding field. Must be false for the members of a union which all start at the same address.</param>
		/// <param name="context">The context of the class the nodes belong to.</param>
		private static List<string> CreateFields(IEnumerable<BaseNode> nodes, bool mergePadding, ClassContext context)
		{
			Contract.Requires(nodes != null);
			Contract.Requires(context != null);

			var fields = new List<string>();
			var usedNames = new HashSet<string>();

			var fill = 0;
			var fillStart = 0;

			// Padding is private, it is no part of the interface of the type.
			void AddPadding(int offset, int size, string comment = null)
			{
				var name = GetUniqueName($"pad_{offset:X04}", usedNames);

				fields.Add($"{name}: [u8; {size}], // 0x{offset:X04}{(comment == null ? string.Empty : " " + comment)}");
			}

			void FlushPadding()
			{
				if (fill != 0)
				{
					AddPadding(fillStart, fill);

					fill = 0;
				}
			}

			foreach (var member in nodes)
			{
				if (member is BaseHexNode)
				{
					if (fill == 0)
					{
						fillStart = member.Offset;
					}
					fill += member.MemorySize;

					if (!mergePadding)
					{
						FlushPadding();
					}

					continue;
				}

				FlushPadding();

				var type = GetTypeDefinition(member, context);
				if (type == null)
				{
					context.Logger.Log(LogLevel.Warning, $"Skipping node with unhandled type: {member.GetType()}");

					// Keep the layout of the following fields intact.
					if (member.MemorySize > 0)
					{
						AddPadding(member.Offset, member.MemorySize, $"Unhandled type: {member.GetType().Name}");
					}

					continue;
				}

				var name = GetUniqueName(GetIdentifier(member.Name, $"field_{member.Offset:X04}"), usedNames);
				var comment = string.IsNullOrEmpty(member.Comment) ? string.Empty : " " + ToSingleLine(member.Comment);

				fields.Add($"pub {name}: {type}, // 0x{member.Offset:X04}{comment}");
			}

			FlushPadding();

			return fields;
		}

		/// <summary>
		/// Gets the Rust type for the given node. Unions and virtual method tables are added to the context as new items.
		/// </summary>
		/// <param name="node">The node.</param>
		/// <param name="context">The context of the class the node belongs to.</param>
		/// <returns>The type or null if the node can't be expressed.</returns>
		private static string GetTypeDefinition(BaseNode node, ClassContext context)
		{
			switch (node)
			{
				case null:
					return null;
				case BaseTextNode textNode:
					return $"[{GetCharacterType(textNode.Encoding)}; {textNode.Length}]";
				case BaseTextPtrNode textPtrNode:
					return $"*const {GetCharacterType(textPtrNode.Encoding)}";
				case BitFieldNode bitFieldNode:
					return GetTypeDefinition(bitFieldNode.GetUnderlayingNode(), context);
				case BaseHexNode hexNode:
					return $"[u8; {hexNode.MemorySize}]";
				case EnumNode enumNode:
					return GetIdentifier(enumNode.Enum.Name, "Enum");
				case TemplateParameterNode parameterNode:
					return EscapeKeyword(parameterNode.ParameterName);
				case TemplateImplementationNode implementationNode:
					return GetTemplateTypeName(implementationNode, context);
				case ClassNode classNode:
					return GetIdentifier(classNode.Name, "Class");
				case BaseClassWrapperNode classWrapperNode:
					return classWrapperNode.InnerNode == null ? null : GetIdentifier(classWrapperNode.InnerNode.Name, "Class");
				case PointerNode _:
				case ArrayBufferNode _:
					return $"*mut {GetTypeDefinition(((BaseWrapperNode)node).InnerNode, context) ?? VoidType}";
				case ArrayNode arrayNode:
				{
					var innerType = GetTypeDefinition(arrayNode.InnerNode, context);

					return innerType == null ? null : $"[{innerType}; {arrayNode.Count}]";
				}
				case UnionNode unionNode:
					return CreateUnion(unionNode, context);
				case VirtualMethodTableNode vtableNode:
					return CreateVirtualMethodTable(vtableNode, context);
			}

			return nodeTypeToTypeDefinationMap.TryGetValue(node.GetType(), out var type) ? type : null;
		}

		/// <summary>
		/// Gets the Rust type of a template implementation, e.g. TArray&lt;Skillbar&gt;.
		/// </summary>
		private static string GetTemplateTypeName(TemplateImplementationNode implementation, ClassContext context)
		{
			var template = implementation.Template;
			if (template == null)
			{
				return null;
			}

			var name = GetIdentifier(template.Name, "Class");
			if (template.Parameters.Count == 0)
			{
				return name;
			}

			string GetArgumentType(string parameter)
			{
				// The unit type stands in for a missing argument because it fulfills the bounds of the parameter.
				return implementation.Arguments.TryGetValue(parameter, out var argument)
					? GetTypeDefinition(argument, context) ?? "()"
					: "()";
			}

			return $"{name}<{string.Join(", ", template.Parameters.Select(GetArgumentType))}>";
		}

		/// <summary>
		/// Adds a union type for the node to the context. Rust has no anonymous unions.
		/// </summary>
		/// <returns>The type of the union or null if the union has no members.</returns>
		private static string CreateUnion(UnionNode unionNode, ClassContext context)
		{
			var name = EscapeKeyword(GetUniqueName($"{context.ClassName}_union_{unionNode.Offset:X04}", context.TypeNames));

			var fields = CreateFields(unionNode.Nodes, false, context);
			if (fields.Count == 0)
			{
				// A union needs at least one field.
				return null;
			}

			// A union in a template is generic over the parameters its members use.
			var parameters = TemplateUtil.CollectParameterNames(unionNode.Nodes).Select(EscapeKeyword).ToList();

			context.Items.Add(new Item { Keyword = "union", Name = name, Parameters = parameters, Fields = fields });

			return parameters.Count == 0 ? name : $"{name}<{string.Join(", ", parameters)}>";
		}

		/// <summary>
		/// Adds a struct with the methods of the virtual method table to the context.
		/// </summary>
		/// <returns>The type of the pointer to the virtual method table.</returns>
		private static string CreateVirtualMethodTable(VirtualMethodTableNode vtableNode, ClassContext context)
		{
			var name = EscapeKeyword(GetUniqueName($"{context.ClassName}_VTable", context.TypeNames));

			var usedNames = new HashSet<string>();
			var fields = vtableNode.Nodes
				.OfType<VirtualMethodNode>()
				.Select(m =>
				{
					var methodName = GetUniqueName(GetIdentifier(m.MethodName, $"Function{m.Offset / IntPtr.Size}"), usedNames);
					var comment = string.IsNullOrEmpty(m.Comment) ? string.Empty : " " + ToSingleLine(m.Comment);

					return $"pub {methodName}: *const {VoidType}, // 0x{m.Offset:X04}{comment}";
				})
				.ToList();

			context.Items.Add(new Item { Keyword = "struct", Name = name, Fields = fields });

			return $"*const {name}";
		}

		private static string GetCharacterType(Encoding encoding)
		{
			if (encoding.IsSameCodePage(Encoding.Unicode))
			{
				return "u16";
			}
			if (encoding.IsSameCodePage(Encoding.UTF32))
			{
				return "u32";
			}
			return "u8";
		}

		/// <summary>Gets a valid Rust identifier for the name.</summary>
		/// <param name="name">The name as used in the project.</param>
		/// <param name="fallback">The identifier to use if the name contains nothing usable.</param>
		private static string GetIdentifier(string name, string fallback)
		{
			return EscapeKeyword(SanitizeName(name, fallback));
		}

		/// <summary>Replaces the characters which are not allowed in an identifier. Keywords are not escaped.</summary>
		private static string SanitizeName(string name, string fallback)
		{
			var sb = new StringBuilder(name?.Length ?? 0);
			foreach (var c in name ?? string.Empty)
			{
				sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
			}

			if (sb.Length == 0 || sb.ToString().All(c => c == '_'))
			{
				return fallback;
			}

			if (char.IsDigit(sb[0]))
			{
				sb.Insert(0, '_');
			}

			return sb.ToString();
		}

		private static string EscapeKeyword(string identifier)
		{
			if (!keywords.Contains(identifier))
			{
				return identifier;
			}

			return keywordsWithoutRawIdentifier.Contains(identifier) ? identifier + "_" : "r#" + identifier;
		}

		/// <summary>Gets the name or the name with a number appended if the name is already used, and marks it as used.</summary>
		private static string GetUniqueName(string name, ISet<string> usedNames)
		{
			var uniqueName = name;
			for (var i = 2; !usedNames.Add(uniqueName); ++i)
			{
				uniqueName = $"{name}_{i}";
			}
			return uniqueName;
		}

		private static string ToSingleLine(string text)
		{
			return text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
		}
	}
}
