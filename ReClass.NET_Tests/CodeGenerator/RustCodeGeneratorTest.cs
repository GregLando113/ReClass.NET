using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moq;
using NFluent;
using ReClassNET.CodeGenerator;
using ReClassNET.Controls;
using ReClassNET.Logger;
using ReClassNET.Nodes;
using ReClassNET.Project;
using Xunit;

namespace ReClass.NET_Tests.CodeGenerator
{
	public class RustCodeGeneratorTest
	{
		private class UnknownNode : BaseNode
		{
			public override int MemorySize => 6;
			public override void GetUserInterfaceInfo(out string name, out Image icon) => throw new NotImplementedException();
			public override Size Draw(DrawContext context, int x, int y) => throw new NotImplementedException();
			public override int CalculateDrawnHeight(DrawContext context) => throw new NotImplementedException();
		}

		private readonly ReClassNetProject project = new ReClassNetProject();

		private ClassNode CreateClass(string name, params BaseNode[] nodes)
		{
			var classNode = ClassNode.Create();
			classNode.Name = name;
			project.AddClass(classNode);
			classNode.AddNodes(nodes);
			return classNode;
		}

		private TemplateNode CreateTemplate(string name, params BaseNode[] nodes)
		{
			var template = TemplateNode.Create();
			template.Name = name;
			project.AddTemplate(template);
			template.AddNodes(nodes);
			return template;
		}

		private static ClassInstanceNode Instance(ClassNode classNode, string name = "instance")
		{
			var instance = new ClassInstanceNode { Name = name };
			instance.ChangeInnerNode(classNode);
			return instance;
		}

		private static T Wrap<T>(BaseNode inner, string name) where T : BaseWrapperNode, new()
		{
			var wrapper = new T { Name = name };
			wrapper.ChangeInnerNode(inner);
			return wrapper;
		}

		private TemplateImplementationNode Implement(TemplateNode template, string name, params (string, BaseNode)[] arguments)
		{
			var implementation = new TemplateImplementationNode { Name = name };
			implementation.SetTemplate(template, arguments.Select(a => new KeyValuePair<string, BaseNode>(a.Item1, a.Item2)));
			implementation.Rebuild(project);
			return implementation;
		}

		private static EnumDescription CreateEnum(string name, bool flags, EnumDescription.UnderlyingTypeSize size, params (string, long)[] values)
		{
			var @enum = new EnumDescription { Name = name };
			@enum.SetData(flags, size, values.Select(v => new KeyValuePair<string, long>(v.Item1, v.Item2)));
			return @enum;
		}

		private static string Generate(IReadOnlyList<ClassNode> classes, params EnumDescription[] enums)
		{
			return new RustCodeGenerator().GenerateCode(classes, enums, new NullLogger());
		}

		[Fact]
		public void LanguageIsRust()
		{
			Check.That(new RustCodeGenerator().Language).IsEqualTo(Language.Rust);
		}

		[Fact]
		public void WritesStructWithOffsetsAndSizeAssertion()
		{
			var classNode = CreateClass(
				"Player",
				new Int32Node { Name = "health", Comment = "current\r\nvalue" },
				new FloatNode { Name = "speed" },
				new UInt64Node { Name = "id" }
			);
			classNode.Comment = "The local player";

			var code = Generate(new[] { classNode });

			Check.That(code).Contains("// The local player");
			Check.That(code).Contains("#[repr(C)]");
			Check.That(code).Contains("#[derive(Clone, Copy)]");
			Check.That(code).Contains("pub struct Player {");
			Check.That(code).Contains("    pub health: i32, // 0x0000 current value");
			Check.That(code).Contains("    pub speed: f32, // 0x0004");
			Check.That(code).Contains("    pub id: u64, // 0x0008");
			Check.That(code).Contains("} // Size: 0x0010");
			Check.That(code).Contains("const _: () = assert!(core::mem::size_of::<Player>() == 0x10);");
		}

		[Fact]
		public void PaddingIsMergedAndPrivate()
		{
			var classNode = CreateClass(
				"Padded",
				new Int32Node { Name = "first" },
				new Hex32Node(),
				new Hex64Node(),
				new Int32Node { Name = "second" },
				new Hex16Node()
			);

			var code = Generate(new[] { classNode });

			Check.That(code).Contains("    pad_0004: [u8; 12], // 0x0004");
			Check.That(code).Contains("    pad_0014: [u8; 2], // 0x0014");
			Check.That(code).DoesNotContain("pub pad_");
		}

		[Fact]
		public void WritesPointersAndArrays()
		{
			var target = CreateClass("Target", new Int32Node { Name = "value" });
			var classNode = CreateClass(
				"Owner",
				Wrap<PointerNode>(Instance(target), "target"),
				new PointerNode { Name = "unknown" },
				Wrap<ArrayNode>(new Int16Node(), "numbers"),
				Wrap<ArrayBufferNode>(Instance(target), "buffer"),
				Wrap<PointerNode>(Wrap<ArrayNode>(new FloatNode(), "inner"), "pointerToArray"),
				Instance(target, "embedded")
			);
			((ArrayNode)classNode.Nodes[2]).Count = 4;

			var code = Generate(new[] { classNode });

			Check.That(code).Contains("pub target: *mut Target,");
			Check.That(code).Contains("pub unknown: *mut core::ffi::c_void,");
			Check.That(code).Contains("pub numbers: [i16; 4],");
			Check.That(code).Contains("pub buffer: *mut Target,");
			Check.That(code).Contains("pub pointerToArray: *mut [f32; 1],");
			Check.That(code).Contains("pub embedded: Target,");
		}

		[Fact]
		public void WritesTextBitFieldAndMathNodes()
		{
			var classNode = CreateClass(
				"Mixed",
				new Utf8TextNode { Name = "name", Length = 16 },
				new Utf16TextNode { Name = "wideName", Length = 8 },
				new Utf32TextNode { Name = "veryWideName", Length = 2 },
				new Utf8TextPtrNode { Name = "namePtr" },
				new Utf16TextPtrNode { Name = "wideNamePtr" },
				new BitFieldNode { Name = "bits", Bits = 16 },
				new Vector3Node { Name = "position" },
				new Matrix3x4Node { Name = "transform" },
				new FunctionPtrNode { Name = "callback" },
				new BoolNode { Name = "enabled" },
				new NIntNode { Name = "native" }
			);

			var code = Generate(new[] { classNode });

			Check.That(code).Contains("pub name: [u8; 16],");
			Check.That(code).Contains("pub wideName: [u16; 8],");
			Check.That(code).Contains("pub veryWideName: [u32; 2],");
			Check.That(code).Contains("pub namePtr: *const u8,");
			Check.That(code).Contains("pub wideNamePtr: *const u16,");
			Check.That(code).Contains("pub bits: u16,");
			Check.That(code).Contains("pub position: [f32; 3],");
			Check.That(code).Contains("pub transform: [[f32; 4]; 3],");
			Check.That(code).Contains("pub callback: *const core::ffi::c_void,");
			Check.That(code).Contains("pub enabled: bool,");
			Check.That(code).Contains("pub native: isize,");
		}

		[Fact]
		public void UnionBecomesNamedType()
		{
			var union = new UnionNode { Name = "value" };
			var classNode = CreateClass("Variant", new Int32Node { Name = "kind" }, union);
			union.AddNode(new Int32Node { Name = "asInt" });
			union.AddNode(new FloatNode { Name = "asFloat" });
			union.AddNode(new Hex16Node());
			union.AddNode(new Hex16Node());

			var code = Generate(new[] { classNode });

			Check.That(code).Contains("pub union Variant_union_0004 {");
			Check.That(code).Contains("    pub asInt: i32, // 0x0000");
			Check.That(code).Contains("    pub asFloat: f32,");
			// The members of a union overlap, so the padding can't be merged.
			Check.That(code).Contains("    pad_0000: [u8; 2],");
			Check.That(code).Contains("    pad_0000_2: [u8; 2],");
			Check.That(code).Contains("    pub value: Variant_union_0004, // 0x0004");
			Check.That(code.IndexOf("pub union Variant_union_0004", StringComparison.Ordinal))
				.IsStrictlyLessThan(code.IndexOf("pub struct Variant", StringComparison.Ordinal));
		}

		[Fact]
		public void VirtualMethodTableBecomesStructOfPointers()
		{
			var vtable = new VirtualMethodTableNode { Name = "vtable" };
			var classNode = CreateClass("Actor", vtable, new Int32Node { Name = "id" });
			vtable.AddNode(new VirtualMethodNode());
			vtable.AddNode(new VirtualMethodNode { Name = "Update" });

			var code = Generate(new[] { classNode });

			Check.That(code).Contains("pub struct Actor_VTable {");
			Check.That(code).Contains("    pub Function0: *const core::ffi::c_void, // 0x0000");
			Check.That(code).Contains("    pub Update: *const core::ffi::c_void,");
			Check.That(code).Contains("    pub vtable: *const Actor_VTable, // 0x0000");
		}

		[Fact]
		public void WritesEnum()
		{
			var team = CreateEnum("Team", false, EnumDescription.UnderlyingTypeSize.FourBytes, ("Red", 0), ("Blue", 1), ("Default", 0), ("Lost", -1));
			var enumNode = new EnumNode { Name = "team" };
			enumNode.ChangeEnum(team);
			var classNode = CreateClass("Member", enumNode);

			var code = Generate(new[] { classNode }, team);

			Check.That(code).Contains("#[repr(i32)]");
			Check.That(code).Contains("#[derive(Clone, Copy, PartialEq, Eq, Debug)]");
			Check.That(code).Contains("pub enum Team {");
			Check.That(code).Contains("    Lost = -1,");
			Check.That(code).Contains("    Red = 0,");
			Check.That(code).Contains("    Blue = 1,");
			// Rust doesn't allow a second variant with the same value.
			Check.That(code).DoesNotContain("Default = 0");
			Check.That(code).Contains("impl Team {");
			Check.That(code).Contains("    pub const Default: Self = Self::Red;");
			Check.That(code).Contains("pub team: Team,");
		}

		[Fact]
		public void FlagsEnumIsUnsigned()
		{
			var flags = CreateEnum("Flags", true, EnumDescription.UnderlyingTypeSize.OneByte, ("None", 0), ("All", 0xFF));

			var code = Generate(Array.Empty<ClassNode>(), flags);

			Check.That(code).Contains("#[repr(u8)]");
			Check.That(code).Contains("    All = 255,");
		}

		[Fact]
		public void TemplateBecomesGenericStruct()
		{
			var element = CreateClass("Element", new Int32Node { Name = "value" });
			var template = CreateTemplate(
				"TArray",
				Wrap<PointerNode>(new TemplateParameterNode { ParameterName = "T" }, "data"),
				new UInt32Node { Name = "len" }
			);
			var owner = CreateClass("Owner", Implement(template, "elements", ("T", Instance(element))), Implement(template, "numbers", ("T", new FloatNode())));

			// Only the owner is passed, the template and the argument class are dependencies.
			var code = Generate(new[] { owner });

			Check.That(code).Contains("pub struct TArray<T: Copy> {");
			Check.That(code).Contains("    pub data: *mut T, // 0x0000");
			Check.That(code).DoesNotContain("size_of::<TArray");
			Check.That(code).Contains("pub struct Element {");
			Check.That(code).Contains("    pub elements: TArray<Element>, // 0x0000");
			Check.That(code).Contains("    pub numbers: TArray<f32>,");
			Check.That(code).Contains("size_of::<Owner>()");
		}

		[Fact]
		public void NamesAreValidIdentifiers()
		{
			var classNode = CreateClass(
				"My Class",
				new Int32Node { Name = "type" },
				new Int32Node { Name = "self" },
				new Int32Node { Name = "1st value" },
				new Int32Node { Name = "" },
				new Int32Node { Name = "same" },
				new Int32Node { Name = "same" }
			);

			var code = Generate(new[] { classNode });

			Check.That(code).Contains("pub struct My_Class {");
			Check.That(code).Contains("pub r#type: i32,");
			Check.That(code).Contains("pub self_: i32,");
			Check.That(code).Contains("pub _1st_value: i32,");
			Check.That(code).Contains("pub field_000C: i32,");
			Check.That(code).Contains("pub same: i32,");
			Check.That(code).Contains("pub same_2: i32,");
		}

		[Fact]
		public void UnhandledNodeIsLoggedAndKeepsTheLayout()
		{
			var classNode = CreateClass("Custom", new Int16Node { Name = "before" }, new UnknownNode { Name = "custom" }, new Int32Node { Name = "after" });
			var logger = new Mock<ILogger>();

			var code = new RustCodeGenerator().GenerateCode(new[] { classNode }, Array.Empty<EnumDescription>(), logger.Object);

			logger.Verify(l => l.Log(LogLevel.Warning, It.Is<string>(s => s.Contains(nameof(UnknownNode)))), Times.Once());
			Check.That(code).Contains("    pad_0002: [u8; 6], // 0x0002 Unhandled type: UnknownNode");
			Check.That(code).Contains("    pub after: i32, // 0x0008");
		}

		[Fact]
		public void ClassWithFunctionIsSkipped()
		{
			var classNode = CreateClass("Code", new FunctionNode());

			var code = Generate(new[] { classNode });

			Check.That(code).DoesNotContain("struct Code");
		}
	}
}
