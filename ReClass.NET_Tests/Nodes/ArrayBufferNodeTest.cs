using System;
using System.IO;
using System.Linq;
using NFluent;
using ReClassNET.CodeGenerator;
using ReClassNET.DataExchange.ReClass;
using ReClassNET.Logger;
using ReClassNET.Memory;
using ReClassNET.Nodes;
using ReClassNET.Project;
using Xunit;

namespace ReClass.NET_Tests.Nodes
{
	public class ArrayBufferNodeTest
	{
		private readonly ReClassNetProject project = new ReClassNetProject();

		private ClassNode CreateClass(params BaseNode[] nodes)
		{
			var classNode = ClassNode.Create();
			project.AddClass(classNode);
			classNode.AddNodes(nodes);
			return classNode;
		}

		private static ArrayBufferNode CreateBuffer(BaseNode elementNode)
		{
			var buffer = new ArrayBufferNode { Name = "data" };
			buffer.ChangeInnerNode(elementNode);
			return buffer;
		}

		private static MemoryBuffer CreateMemory(ClassNode classNode, BaseNode field, byte[] value)
		{
			var memory = new MemoryBuffer { Size = classNode.MemorySize };
			value.CopyTo(memory.RawData, field.Offset);
			return memory;
		}

		[Fact]
		public void MemorySizeIsPointerSize()
		{
			var buffer = CreateBuffer(new Int32Node());
			buffer.Count = 100;

			Check.That(buffer.MemorySize).IsEqualTo(IntPtr.Size);

			buffer.ChangeInnerNode(new Matrix4x4Node());

			Check.That(buffer.MemorySize).IsEqualTo(IntPtr.Size);
		}

		[Fact]
		public void ReplacingPointerSizedHexNodeKeepsLayout()
		{
			var tail = new Int32Node();
			var classNode = CreateClass(IntPtr.Size == 4 ? (BaseNode)new Hex32Node() : new Hex64Node(), tail);

			Check.That(StructureLayout.CanResize(classNode.Nodes[0], IntPtr.Size, out _)).IsTrue();

			var buffer = new ArrayBufferNode();
			buffer.Initialize();
			classNode.ReplaceChildNode(classNode.Nodes[0], buffer);

			Check.That(classNode.Nodes).ContainsExactly(buffer, tail);
			Check.That(tail.Offset).IsEqualTo(IntPtr.Size);
		}

		[Fact]
		public void CountIsReadFromBoundField()
		{
			var buffer = CreateBuffer(new Int32Node());
			var len = new UInt32Node { Name = "len" };
			var classNode = CreateClass(buffer, new UInt32Node { Name = "cap" }, len);

			Check.That(buffer.BindCountField("len", out _)).IsTrue();

			buffer.UpdateCountFromField(CreateMemory(classNode, len, BitConverter.GetBytes(5u)));

			Check.That(buffer.Count).IsEqualTo(5);
			Check.That(buffer.CountFieldName).IsEqualTo("len");
		}

		[Fact]
		public void ZeroCountResetsIndex()
		{
			var buffer = CreateBuffer(new Int32Node());
			var len = new UInt32Node { Name = "len" };
			var classNode = CreateClass(buffer, len);
			buffer.BindCountField("len", out _);
			buffer.Count = 10;
			buffer.CurrentIndex = 3;

			buffer.UpdateCountFromField(CreateMemory(classNode, len, BitConverter.GetBytes(0u)));

			Check.That(buffer.Count).IsEqualTo(0);
			Check.That(buffer.CurrentIndex).IsEqualTo(0);
		}

		[Fact]
		public void NegativeCountIsClampedToZero()
		{
			var buffer = CreateBuffer(new Int32Node());
			var len = new Int32Node { Name = "len" };
			var classNode = CreateClass(buffer, len);
			buffer.BindCountField("len", out _);

			buffer.UpdateCountFromField(CreateMemory(classNode, len, BitConverter.GetBytes(-1)));

			Check.That(buffer.Count).IsEqualTo(0);
		}

		[Fact]
		public void CurrentIndexIsClampedToCount()
		{
			var buffer = CreateBuffer(new Int32Node());
			var len = new UInt32Node { Name = "len" };
			var classNode = CreateClass(buffer, len);
			buffer.BindCountField("len", out _);
			buffer.Count = 10;
			buffer.CurrentIndex = 8;

			buffer.UpdateCountFromField(CreateMemory(classNode, len, BitConverter.GetBytes(4u)));

			Check.That(buffer.CurrentIndex).IsEqualTo(3);
		}

		[Fact]
		public void BindingSurvivesRename()
		{
			var buffer = CreateBuffer(new Int32Node());
			var len = new UInt32Node { Name = "len" };
			var classNode = CreateClass(buffer, len);
			buffer.BindCountField("len", out _);
			buffer.UpdateCountFromField(CreateMemory(classNode, len, BitConverter.GetBytes(2u)));

			len.Name = "size";
			buffer.UpdateCountFromField(CreateMemory(classNode, len, BitConverter.GetBytes(7u)));

			Check.That(buffer.Count).IsEqualTo(7);
			Check.That(buffer.CountFieldName).IsEqualTo("size");
		}

		[Fact]
		public void BindingSurvivesTypeChange()
		{
			var buffer = CreateBuffer(new Int32Node());
			var classNode = CreateClass(buffer, new UInt32Node { Name = "len" });
			buffer.BindCountField("len", out _);

			var newLen = new Int32Node();
			classNode.ReplaceChildNode(classNode.Nodes[1], newLen);
			buffer.UpdateCountFromField(CreateMemory(classNode, newLen, BitConverter.GetBytes(9)));

			Check.That(newLen.Name).IsEqualTo("len");
			Check.That(buffer.Count).IsEqualTo(9);
		}

		[Fact]
		public void UnresolvedFieldKeepsLastCount()
		{
			var buffer = CreateBuffer(new Int32Node());
			var len = new UInt32Node { Name = "len" };
			var classNode = CreateClass(buffer, len);
			buffer.BindCountField("len", out _);
			buffer.UpdateCountFromField(CreateMemory(classNode, len, BitConverter.GetBytes(5u)));

			classNode.UndefineOrRemoveNode(len);
			buffer.UpdateCountFromField(new MemoryBuffer { Size = classNode.MemorySize });

			Check.That(buffer.Count).IsEqualTo(5);
			Check.That(buffer.CountFieldName).IsEqualTo("len");
		}

		[Fact]
		public void CanBindCountFieldRefusesInvalidFields()
		{
			var buffer = CreateBuffer(new Int32Node());
			CreateClass(buffer, new FloatNode { Name = "f" }, new UInt32Node { Name = "len" });

			Check.That(buffer.CanBindCountField("missing", out var reason)).IsFalse();
			Check.That(reason).Contains("missing");
			Check.That(buffer.CanBindCountField("f", out _)).IsFalse();
			Check.That(buffer.CanBindCountField("len", out _)).IsTrue();
		}

		[Fact]
		public void CanBindCountFieldRefusesWrappedBuffer()
		{
			var buffer = CreateBuffer(new Int32Node());
			var array = new ArrayNode();
			array.ChangeInnerNode(buffer);
			CreateClass(array, new UInt32Node { Name = "len" });

			Check.That(buffer.CanBindCountField("len", out _)).IsFalse();
		}

		[Fact]
		public void SerializationKeepsCountField()
		{
			var buffer = CreateBuffer(new Int32Node());
			buffer.Count = 3;
			var len = new UInt32Node { Name = "len" };
			CreateClass(buffer, new UInt32Node { Name = "cap" }, len);
			buffer.BindCountField("len", out _);

			byte[] data;
			using (var output = new MemoryStream())
			{
				ReClassNetFile.SerializeNodesToStream(output, new BaseNode[] { buffer, len }, new NullLogger());
				data = output.ToArray();
			}

			using var input = new MemoryStream(data);
			var (_, nodes) = ReClassNetFile.DeserializeNodesFromStream(input, null, new NullLogger());

			Check.That(nodes[0]).IsInstanceOf<ArrayBufferNode>();
			var loaded = (ArrayBufferNode)nodes[0];
			Check.That(loaded.CountFieldName).IsEqualTo("len");
			Check.That(loaded.Count).IsEqualTo(3);
			Check.That(loaded.InnerNode).IsInstanceOf<Int32Node>();
		}

		[Fact]
		public void CppCodeGeneratorWritesPlainPointer()
		{
			var buffer = CreateBuffer(new Int32Node());
			var classNode = CreateClass(buffer, new UInt32Node { Name = "cap" }, new UInt32Node { Name = "len" });
			classNode.Name = "TArrayTest";

			var code = new CppCodeGenerator(project.TypeMapping).GenerateCode(new[] { classNode }, Array.Empty<EnumDescription>(), new NullLogger());

			Check.That(code).Contains("*data;");
			Check.That(code).DoesNotContain("data[");
		}

		[Fact]
		public void CppCodeGeneratorHandlesSelfReference()
		{
			var classNode = ClassNode.Create();
			classNode.Name = "TreeNode";
			project.AddClass(classNode);
			var instance = new ClassInstanceNode();
			instance.ChangeInnerNode(classNode);
			var buffer = CreateBuffer(instance);
			buffer.Name = "children";
			classNode.AddNodes(new BaseNode[] { buffer, new UInt32Node { Name = "len" } });

			var code = new CppCodeGenerator(project.TypeMapping).GenerateCode(new[] { classNode }, Array.Empty<EnumDescription>(), new NullLogger());

			Check.That(code).Contains("TreeNode *children;");
		}
	}
}
