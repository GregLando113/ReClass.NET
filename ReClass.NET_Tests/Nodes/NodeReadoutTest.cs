using System;
using System.Collections.Generic;
using System.Linq;
using NFluent;
using ReClassNET.Memory;
using ReClassNET.Nodes;
using ReClassNET.Project;
using ReClassNET.Util.Conversion;
using Xunit;

namespace ReClass.NET_Tests.Nodes
{
	public class NodeReadoutTest
	{
		/// <summary>Serves memory from byte arrays placed at fixed addresses.</summary>
		private class FakeReader : IRemoteMemoryReader
		{
			private readonly Dictionary<long, byte[]> regions = new Dictionary<long, byte[]>();

			public EndianBitConverter BitConverter { get; set; } = EndianBitConverter.System;

			public void Add(long address, byte[] data) => regions[address] = data;

			public bool ReadRemoteMemoryIntoBuffer(IntPtr address, ref byte[] buffer)
			{
				return ReadRemoteMemoryIntoBuffer(address, ref buffer, 0, buffer.Length);
			}

			public bool ReadRemoteMemoryIntoBuffer(IntPtr address, ref byte[] buffer, int offset, int length)
			{
				foreach (var kv in regions)
				{
					var start = address.ToInt64() - kv.Key;
					if (start >= 0 && start < kv.Value.Length)
					{
						Array.Clear(buffer, offset, length);
						Array.Copy(kv.Value, start, buffer, offset, Math.Min(length, kv.Value.Length - start));
						return true;
					}
				}

				return false;
			}

			public byte[] ReadRemoteMemory(IntPtr address, int size)
			{
				var buffer = new byte[size];
				ReadRemoteMemoryIntoBuffer(address, ref buffer);
				return buffer;
			}

			public string ReadRemoteRuntimeTypeInformation(IntPtr address) => null;
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

		private static ClassInstanceNode Instance(ClassNode classNode, string name)
		{
			var instance = new ClassInstanceNode { Name = name };
			instance.ChangeInnerNode(classNode);
			return instance;
		}

		private static MemoryBuffer CreateMemory(params byte[][] parts)
		{
			var data = parts.SelectMany(p => p).ToArray();
			var memory = new MemoryBuffer { Size = data.Length };
			data.CopyTo(memory.RawData, 0);
			return memory;
		}

		private static byte[] Pointer(long value)
		{
			return IntPtr.Size == 4 ? BitConverter.GetBytes((int)value) : BitConverter.GetBytes(value);
		}

		private static string[] Lines(string text)
		{
			return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
		}

		[Fact]
		public void FieldsAreListedWithNameAndValue()
		{
			var classNode = CreateClass(
				"Player",
				new Int32Node { Name = "id" },
				new FloatNode { Name = "health" },
				new PointerNode { Name = "target" },
				new Hex16Node { Name = "pad" },
				new Utf8TextNode { Name = "name", Length = 4 }
			);
			var memory = CreateMemory(BitConverter.GetBytes(42), BitConverter.GetBytes(1.5f), Pointer(0x1234), new byte[] { 0xAB, 0xCD }, new[] { (byte)'B', (byte)'o', (byte)'b', (byte)0 });

			var text = NodeReadout.CreateMarkdown(new[] { new NodeReadout.Item(classNode, memory, (IntPtr)0x1000) }, null);
			var lines = Lines(text);

			Check.That(lines[0]).StartsWith("## Player @ 0x").And.EndsWith("1000");
			Check.That(lines).Contains("| Offset | Type | Name | Value |");
			Check.That(lines).Contains("| 0x0000 | Int32 | id | 42 |");
			Check.That(lines).Contains("| 0x0004 | Float | health | 1.500 |");
			Check.That(lines.Single(l => l.Contains("| target |"))).EndsWith("1234 |");
			Check.That(lines).Contains($"| 0x{8 + IntPtr.Size:X04} | Hex16 | pad | AB CD |");
			Check.That(lines).Contains($"| 0x{10 + IntPtr.Size:X04} | Utf8Text | name | \"Bob\" |");
		}

		[Fact]
		public void SelectedFieldsAreGroupedByClass()
		{
			var classNode = CreateClass("Player", new Int32Node { Name = "id" }, new Int32Node { Name = "level" }, new Int32Node { Name = "gold" });
			var memory = CreateMemory(BitConverter.GetBytes(1), BitConverter.GetBytes(2), BitConverter.GetBytes(3));

			var text = NodeReadout.CreateMarkdown(
				new[]
				{
					new NodeReadout.Item(classNode.Nodes[2], memory, (IntPtr)0x1008),
					new NodeReadout.Item(classNode.Nodes[1], memory, (IntPtr)0x1004)
				},
				null
			);
			var lines = Lines(text);

			Check.That(lines.Count(l => l.StartsWith("## "))).IsEqualTo(1);
			Check.That(lines[0]).StartsWith("## Player @ 0x").And.EndsWith("1000");
			Check.That(text).Contains("| 0x0004 | Int32 | level | 2 |");
			Check.That(text).Contains("| 0x0008 | Int32 | gold | 3 |");
			Check.That(text).DoesNotContain("| id |");
		}

		[Fact]
		public void NestedClassGetsItsOwnTable()
		{
			var position = CreateClass("Vec2", new FloatNode { Name = "x" }, new FloatNode { Name = "y" });
			var classNode = CreateClass("Player", new Int32Node { Name = "id" }, Instance(position, "pos"));
			var memory = CreateMemory(BitConverter.GetBytes(7), BitConverter.GetBytes(1f), BitConverter.GetBytes(2f));

			var text = NodeReadout.CreateMarkdown(new[] { new NodeReadout.Item(classNode, memory, (IntPtr)0x1000) }, null);
			var lines = Lines(text);

			Check.That(lines).Contains("| 0x0004 | Vec2 | pos | {Vec2} |");
			Check.That(lines.Single(l => l.StartsWith("### "))).StartsWith("### pos : Vec2 @ 0x").And.EndsWith("1004");
			Check.That(lines).Contains("| 0x0000 | Float | x | 1.000 |");
			Check.That(lines).Contains("| 0x0004 | Float | y | 2.000 |");
		}

		[Fact]
		public void ArrayOfClassesIsATableWithFieldsAsColumns()
		{
			var position = CreateClass("Vec2", new FloatNode { Name = "x" }, new FloatNode { Name = "y" });
			var element = CreateClass("Item", new Int32Node { Name = "id" }, Instance(position, "pos"));
			var array = new ArrayNode { Name = "items", Count = 2 };
			array.ChangeInnerNode(Instance(element, "element"));
			var classNode = CreateClass("Bag", new Int32Node { Name = "count" }, array);
			var memory = CreateMemory(
				BitConverter.GetBytes(2),
				BitConverter.GetBytes(10), BitConverter.GetBytes(1f), BitConverter.GetBytes(2f),
				BitConverter.GetBytes(11), BitConverter.GetBytes(3f), BitConverter.GetBytes(4f)
			);

			var text = NodeReadout.CreateMarkdown(new[] { new NodeReadout.Item(classNode, memory, (IntPtr)0x1000) }, null);
			var lines = Lines(text);

			Check.That(lines).Contains("| 0x0004 | Item[2] | items | [2] |");
			Check.That(lines.Single(l => l.StartsWith("### "))).StartsWith("### items : Item[2] @ 0x");
			Check.That(lines).Contains("| # | id | pos.x | pos.y |");
			Check.That(lines).Contains("| --- | --- | --- | --- |");
			Check.That(lines).Contains("| 0 | 10 | 1.000 | 2.000 |");
			Check.That(lines).Contains("| 1 | 11 | 3.000 | 4.000 |");
		}

		[Fact]
		public void ShortArrayOfValuesIsPrintedInline()
		{
			var array = new ArrayNode { Name = "values", Count = 3 };
			array.ChangeInnerNode(new Int16Node());
			var classNode = CreateClass("Data", array);
			var memory = CreateMemory(BitConverter.GetBytes((short)1), BitConverter.GetBytes((short)2), BitConverter.GetBytes((short)3));

			var text = NodeReadout.CreateMarkdown(new[] { new NodeReadout.Item(classNode, memory, (IntPtr)0x1000) }, null);

			Check.That(text).Contains("| values | [1, 2, 3] |");
			Check.That(text).DoesNotContain("###");
		}

		[Fact]
		public void ArrayBufferOfClassesIsReadFromTheTarget()
		{
			var element = CreateClass("Skill", new Int32Node { Name = "id" }, new Int32Node { Name = "rank" });
			var buffer = new ArrayBufferNode { Name = "data" };
			buffer.ChangeInnerNode(Instance(element, "element"));
			var classNode = CreateClass("TArray", buffer, new UInt32Node { Name = "cap" }, new UInt32Node { Name = "len" });
			buffer.BindCountField("len", out _);

			var memory = CreateMemory(Pointer(0x5000), BitConverter.GetBytes(8u), BitConverter.GetBytes(2u));
			var reader = new FakeReader();
			reader.Add(0x5000, new[] { 100, 1, 200, 2 }.SelectMany(BitConverter.GetBytes).ToArray());

			var text = NodeReadout.CreateMarkdown(new[] { new NodeReadout.Item(classNode, memory, (IntPtr)0x1000) }, reader);
			var lines = Lines(text);

			Check.That(lines.Single(l => l.Contains("| data |"))).EndsWith("5000 [2] |");
			Check.That(lines.Single(l => l.StartsWith("### "))).StartsWith("### data : Skill[2] @ 0x").And.EndsWith("5000");
			Check.That(lines).Contains("| # | id | rank |");
			Check.That(lines).Contains("| 0 | 100 | 1 |");
			Check.That(lines).Contains("| 1 | 200 | 2 |");

			// Without a readable target only the address is printed.
			var textWithoutReader = NodeReadout.CreateMarkdown(new[] { new NodeReadout.Item(classNode, memory, (IntPtr)0x1000) }, null);
			Check.That(textWithoutReader).DoesNotContain("###");
		}

		[Fact]
		public void SpecialCharactersAreEscaped()
		{
			var classNode = CreateClass("Data", new Int32Node { Name = "a|b" });
			var memory = CreateMemory(BitConverter.GetBytes(1));

			var text = NodeReadout.CreateMarkdown(new[] { new NodeReadout.Item(classNode, memory, (IntPtr)0x1000) }, null);

			Check.That(text).Contains("| a\\|b | 1 |");
		}
	}
}
