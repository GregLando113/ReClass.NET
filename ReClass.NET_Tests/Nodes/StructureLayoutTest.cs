using System.Linq;
using NFluent;
using ReClassNET.Nodes;
using ReClassNET.Project;
using Xunit;

namespace ReClass.NET_Tests.Nodes
{
	public class StructureLayoutTest
	{
		private readonly ReClassNetProject project = new ReClassNetProject();

		private ClassNode CreateClass(params BaseNode[] nodes)
		{
			var classNode = ClassNode.Create();
			project.AddClass(classNode);
			classNode.AddNodes(nodes);
			return classNode;
		}

		private static ClassInstanceNode CreateInstance(ClassNode classNode)
		{
			var instanceNode = new ClassInstanceNode();
			instanceNode.ChangeInnerNode(classNode);
			return instanceNode;
		}

		[Fact]
		public void DeletingTypedNodeKeepsLayout()
		{
			var tail = new Int32Node();
			var classNode = CreateClass(new Int32Node(), new Int32Node(), tail);

			classNode.UndefineOrRemoveNode(classNode.Nodes[1]);

			Check.That(classNode.MemorySize).IsEqualTo(12);
			Check.That(classNode.Nodes[1]).IsInstanceOf<Hex32Node>();
			Check.That(classNode.Nodes[1].Offset).IsEqualTo(4);
			Check.That(tail.Offset).IsEqualTo(8);
		}

		[Fact]
		public void DeletingHexNodeShrinksClass()
		{
			var tail = new Int32Node();
			var classNode = CreateClass(new Int32Node(), new Hex32Node(), tail);

			classNode.UndefineOrRemoveNode(classNode.Nodes[1]);

			Check.That(classNode.MemorySize).IsEqualTo(8);
			Check.That(tail.Offset).IsEqualTo(4);
		}

		[Fact]
		public void ReplacingWithLargerNodeAbsorbsFollowingHexNodes()
		{
			var tail = new Int32Node();
			var classNode = CreateClass(Enumerable.Range(0, 8).Select(_ => (BaseNode)new Hex8Node()).Append(tail).ToArray());

			Check.That(StructureLayout.CanResize(classNode.Nodes[0], 8, out _)).IsTrue();

			var newNode = new Int64Node();
			classNode.ReplaceChildNode(classNode.Nodes[0], newNode);

			Check.That(classNode.Nodes).ContainsExactly(newNode, tail);
			Check.That(classNode.MemorySize).IsEqualTo(12);
			Check.That(tail.Offset).IsEqualTo(8);
		}

		[Fact]
		public void ReplacingWithLargerNodeSplitsPartialHexNode()
		{
			var tail = new Int32Node();
			var classNode = CreateClass(new Hex32Node(), new Hex64Node(), tail);

			var newNode = new Int64Node();
			classNode.ReplaceChildNode(classNode.Nodes[0], newNode);

			Check.That(classNode.Nodes.Count).IsEqualTo(3);
			Check.That(classNode.Nodes[1]).IsInstanceOf<Hex32Node>();
			Check.That(classNode.Nodes[1].Offset).IsEqualTo(8);
			Check.That(tail.Offset).IsEqualTo(12);
		}

		[Fact]
		public void ReplacingWithLargerNodeIsRefusedWithoutHexBytes()
		{
			var classNode = CreateClass(new Hex8Node(), new Int32Node());

			Check.That(StructureLayout.CanResize(classNode.Nodes[0], 8, out var reason)).IsFalse();
			Check.That(reason).IsNotEmpty();
		}

		[Fact]
		public void GrowingEmbeddedClassAbsorbsHexBytes()
		{
			var embedded = CreateClass(new Hex64Node());
			var tail = new Int32Node();
			var instance = CreateInstance(embedded);
			var outer = CreateClass(instance, new Hex64Node(), new Hex64Node(), tail);

			Check.That(StructureLayout.CanResizeClass(embedded, 12, project.Classes, out _)).IsTrue();

			embedded.AddBytes(4);

			Check.That(instance.MemorySize).IsEqualTo(12);
			Check.That(outer.MemorySize).IsEqualTo(28);
			Check.That(outer.Nodes[1]).IsInstanceOf<Hex32Node>();
			Check.That(outer.Nodes[1].Offset).IsEqualTo(12);
			Check.That(tail.Offset).IsEqualTo(24);
		}

		[Fact]
		public void ShrinkingEmbeddedClassPadsWithHexBytes()
		{
			var embedded = CreateClass(new Hex64Node(), new Hex32Node());
			var tail = new Int32Node();
			var instance = CreateInstance(embedded);
			var outer = CreateClass(instance, tail);

			embedded.UndefineOrRemoveNode(embedded.Nodes[1]);

			Check.That(instance.MemorySize).IsEqualTo(8);
			Check.That(outer.MemorySize).IsEqualTo(16);
			Check.That(outer.Nodes[1]).IsInstanceOf<Hex32Node>();
			Check.That(outer.Nodes[1].Offset).IsEqualTo(8);
			Check.That(tail.Offset).IsEqualTo(12);
		}

		[Fact]
		public void GrowingEmbeddedClassInArrayAbsorbsCountTimesBytes()
		{
			var embedded = CreateClass(new Hex64Node());
			var array = new ArrayNode();
			array.ChangeInnerNode(CreateInstance(embedded));
			array.Count = 3;
			var tail = new Int32Node();
			var outer = CreateClass(array, new Hex64Node(), new Hex64Node(), tail);

			Check.That(StructureLayout.CanResizeClass(embedded, 12, project.Classes, out _)).IsTrue();
			Check.That(StructureLayout.CanResizeClass(embedded, 16, project.Classes, out _)).IsFalse();

			embedded.AddBytes(4);

			Check.That(array.MemorySize).IsEqualTo(36);
			Check.That(outer.MemorySize).IsEqualTo(44);
			Check.That(outer.Nodes[1]).IsInstanceOf<Hex32Node>();
			Check.That(outer.Nodes[1].Offset).IsEqualTo(36);
			Check.That(tail.Offset).IsEqualTo(40);
		}

		[Fact]
		public void GrowingEmbeddedClassInUnionAbsorbsAfterUnion()
		{
			var embedded = CreateClass(new Hex64Node());
			var union = new UnionNode();
			union.AddNode(CreateInstance(embedded));
			var tail = new Int32Node();
			var outer = CreateClass(union, new Hex64Node(), tail);

			Check.That(StructureLayout.CanResizeClass(embedded, 16, project.Classes, out _)).IsTrue();
			Check.That(StructureLayout.CanResizeClass(embedded, 20, project.Classes, out _)).IsFalse();

			embedded.AddBytes(4);

			Check.That(union.MemorySize).IsEqualTo(12);
			Check.That(outer.MemorySize).IsEqualTo(20);
			Check.That(outer.Nodes[1]).IsInstanceOf<Hex32Node>();
			Check.That(outer.Nodes[1].Offset).IsEqualTo(12);
			Check.That(tail.Offset).IsEqualTo(16);
		}

		[Fact]
		public void GrowingClassIsRefusedIfEmbeddedAtTheEnd()
		{
			var embedded = CreateClass(new Hex64Node());
			CreateClass(new Int32Node(), CreateInstance(embedded));

			Check.That(StructureLayout.CanResizeClass(embedded, 12, project.Classes, out var reason)).IsFalse();
			Check.That(reason).Contains(embedded.Name);
		}

		[Fact]
		public void EmbeddingThroughPointerDoesNotRestrictGrowth()
		{
			var embedded = CreateClass(new Hex64Node());
			var pointer = new PointerNode();
			pointer.ChangeInnerNode(CreateInstance(embedded));
			CreateClass(new Int32Node(), pointer);

			Check.That(StructureLayout.CanResizeClass(embedded, 12, project.Classes, out _)).IsTrue();
		}

		[Fact]
		public void PastingSameSizeReplacesSelectedNode()
		{
			var tail = new Int32Node();
			var classNode = CreateClass(new Hex64Node(), tail);
			var pasted1 = new Int32Node { Name = "pasted1" };
			var pasted2 = new FloatNode { Name = "pasted2" };

			classNode.ReplaceChildNodeWithNodes(classNode.Nodes[0], new BaseNode[] { pasted1, pasted2 });

			Check.That(classNode.Nodes).ContainsExactly(pasted1, pasted2, tail);
			Check.That(pasted1.Name).IsEqualTo("pasted1");
			Check.That(pasted2.Offset).IsEqualTo(4);
			Check.That(tail.Offset).IsEqualTo(8);
		}

		[Fact]
		public void PastingLargerNodesAbsorbsHexBytes()
		{
			var tail = new Int32Node();
			var classNode = CreateClass(new Hex64Node(), new Hex64Node(), tail);
			var pasted = new BaseNode[] { new Int32Node(), new FloatNode(), new Int32Node() };

			Check.That(StructureLayout.CanResize(classNode.Nodes[0], 12, out _)).IsTrue();

			classNode.ReplaceChildNodeWithNodes(classNode.Nodes[0], pasted);

			Check.That(classNode.MemorySize).IsEqualTo(20);
			Check.That(classNode.Nodes[3]).IsInstanceOf<Hex32Node>();
			Check.That(classNode.Nodes[3].Offset).IsEqualTo(12);
			Check.That(tail.Offset).IsEqualTo(16);
		}

		[Fact]
		public void PastingSmallerNodesPadsWithHexBytes()
		{
			var tail = new Int32Node();
			var classNode = CreateClass(new Hex64Node(), tail);

			classNode.ReplaceChildNodeWithNodes(classNode.Nodes[0], new BaseNode[] { new Int32Node() });

			Check.That(classNode.MemorySize).IsEqualTo(12);
			Check.That(classNode.Nodes[1]).IsInstanceOf<Hex32Node>();
			Check.That(tail.Offset).IsEqualTo(8);
		}

		[Fact]
		public void PastingLargerNodesIsRefusedWithoutHexBytes()
		{
			var classNode = CreateClass(new Hex64Node(), new Int32Node());

			Check.That(StructureLayout.CanResize(classNode.Nodes[0], 12, out _)).IsFalse();
		}

		[Fact]
		public void BulkLoadDoesNotAbsorbHexBytes()
		{
			project.BeginBulkLoad();

			// Like the importers: the class is referenced before its nodes are added.
			var embedded = ClassNode.Create();
			project.AddClass(embedded);
			var tail = new Int32Node();
			var outer = CreateClass(CreateInstance(embedded), new Hex64Node(), tail);
			embedded.AddBytes(8);

			project.EndBulkLoad();

			// A later unrelated change must not compensate the size change from the load.
			outer.AddBytes(4);

			Check.That(outer.Nodes.Count).IsEqualTo(4);
			Check.That(tail.Offset).IsEqualTo(16);
		}
	}
}
