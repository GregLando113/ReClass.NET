using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NFluent;
using ReClassNET.CodeGenerator;
using ReClassNET.DataExchange.ReClass;
using ReClassNET.Logger;
using ReClassNET.Nodes;
using ReClassNET.Project;
using Xunit;

namespace ReClass.NET_Tests.Nodes
{
	public class TemplateTest
	{
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

		private static TemplateParameterNode Parameter(string parameter, string name = "value")
		{
			return new TemplateParameterNode { ParameterName = parameter, Name = name };
		}

		private static ClassInstanceNode Instance(ClassNode classNode)
		{
			var instance = new ClassInstanceNode();
			instance.ChangeInnerNode(classNode);
			return instance;
		}

		private static T Wrap<T>(BaseNode inner, string name = "data") where T : BaseWrapperNode, new()
		{
			var wrapper = new T { Name = name };
			wrapper.ChangeInnerNode(inner);
			return wrapper;
		}

		private TemplateImplementationNode Implement(TemplateNode template, params (string, BaseNode)[] arguments)
		{
			var implementation = new TemplateImplementationNode();
			implementation.SetTemplate(template, arguments.Select(a => new KeyValuePair<string, BaseNode>(a.Item1, a.Item2)));
			implementation.Rebuild(project);
			return implementation;
		}

		/// <summary>TArray&lt;T&gt; { T* data; uint32 cap; uint32 len; }</summary>
		private TemplateNode CreateArrayTemplate()
		{
			return CreateTemplate(
				"TArray",
				Wrap<ArrayBufferNode>(Parameter("T")),
				new UInt32Node { Name = "cap" },
				new UInt32Node { Name = "len" }
			);
		}

		[Fact]
		public void TemplateCollectsParameters()
		{
			var template = CreateTemplate("TPair", Parameter("K", "key"), Wrap<PointerNode>(Parameter("V")), Parameter("K", "other"));

			Check.That(template.Parameters).ContainsExactly("K", "V");
			Check.That(template.DisplayName).IsEqualTo("TPair<K, V>");
			Check.That(template.MemorySize).IsEqualTo(IntPtr.Size);
		}

		[Fact]
		public void ParametersAreOnlyAllowedInTemplates()
		{
			var classNode = CreateClass("Normal");

			Check.That(classNode.CanHandleChildNode(Parameter("T"))).IsFalse();
			Check.That(CreateTemplate("T1").CanHandleChildNode(Parameter("T"))).IsTrue();
		}

		[Fact]
		public void ImplementationResolvesBufferElement()
		{
			var skillbar = CreateClass("Skillbar", new Hex64Node(), new Hex32Node());
			var template = CreateArrayTemplate();

			var implementation = Implement(template, ("T", Instance(skillbar)));

			Check.That(implementation.TypeName).IsEqualTo("TArray<Skillbar>");
			Check.That(implementation.InstanceClass.Name).IsEqualTo("TArray<Skillbar>");
			Check.That(implementation.MemorySize).IsEqualTo(IntPtr.Size + 8);

			var nodes = implementation.InstanceClass.Nodes;
			Check.That(nodes.Count).IsEqualTo(3);
			var buffer = (ArrayBufferNode)nodes[0];
			Check.That(buffer.Name).IsEqualTo("data");
			Check.That(((ClassInstanceNode)buffer.InnerNode).InnerNode).IsSameReferenceAs(skillbar);
			Check.That(nodes[2].Name).IsEqualTo("len");
			Check.That(nodes[2].Offset).IsEqualTo(IntPtr.Size + 4);

			// The template itself is unchanged.
			Check.That(((ArrayBufferNode)template.Nodes[0]).InnerNode).IsInstanceOf<TemplateParameterNode>();
		}

		[Fact]
		public void InlineParameterTakesSizeOfArgument()
		{
			var template = CreateTemplate("TBox", new UInt32Node { Name = "id" }, Parameter("T"), new UInt32Node { Name = "tail" });

			var implementation = Implement(template, ("T", new UInt8Node()));

			Check.That(implementation.MemorySize).IsEqualTo(9);
			Check.That(implementation.InstanceClass.Nodes[1]).IsInstanceOf<UInt8Node>();
			Check.That(implementation.InstanceClass.Nodes[1].Name).IsEqualTo("value");
			Check.That(implementation.InstanceClass.Nodes[2].Offset).IsEqualTo(5);
			Check.That(implementation.TypeName).IsEqualTo("TBox<UInt8>");
		}

		[Fact]
		public void UnsetArgumentStaysPlaceholder()
		{
			var template = CreateTemplate("TBox", new UInt32Node(), Parameter("T"));

			var implementation = Implement(template);

			Check.That(implementation.MemorySize).IsEqualTo(4);
			Check.That(implementation.TypeName).IsEqualTo("TBox<?>");
			Check.That(implementation.InstanceClass.Nodes[1]).IsInstanceOf<TemplateParameterNode>();
		}

		[Fact]
		public void ChangingArgumentMakesOwnerAbsorbAndPad()
		{
			var big = CreateClass("Big", new Hex64Node(), new Hex32Node());
			var template = CreateTemplate("TBox", Parameter("T"));
			var implementation = Implement(template, ("T", new UInt8Node()));
			var tail = new Int32Node();
			var owner = CreateClass("Owner", new BaseNode[] { implementation }.Concat(Enumerable.Range(0, 15).Select(_ => (BaseNode)new Hex8Node())).Append(tail).ToArray());

			Check.That(tail.Offset).IsEqualTo(16);

			implementation.SetTemplate(template, new[] { new KeyValuePair<string, BaseNode>("T", Instance(big)) });
			implementation.Rebuild(project);

			Check.That(implementation.MemorySize).IsEqualTo(12);
			Check.That(tail.Offset).IsEqualTo(16);
			Check.That(owner.MemorySize).IsEqualTo(20);

			implementation.SetTemplate(template, new[] { new KeyValuePair<string, BaseNode>("T", new UInt16Node()) });
			implementation.Rebuild(project);

			Check.That(implementation.MemorySize).IsEqualTo(2);
			Check.That(tail.Offset).IsEqualTo(16);
		}

		[Fact]
		public void EditingTemplateUpdatesImplementations()
		{
			var template = CreateArrayTemplate();
			var implementation1 = Implement(template, ("T", new UInt8Node()));
			var implementation2 = Implement(template, ("T", new FloatNode()));
			var tail1 = new Int32Node();
			var tail2 = new Int32Node();
			CreateClass("Owner1", implementation1, new Hex64Node(), tail1);
			CreateClass("Owner2", implementation2, new Hex32Node(), tail2);
			var size = implementation1.MemorySize;

			Check.That(StructureLayout.CanResizeClass(template, template.MemorySize + 4, project.Classes, out _)).IsTrue();

			template.AddBytes(4);

			Check.That(implementation1.MemorySize).IsEqualTo(size + 4);
			Check.That(implementation2.MemorySize).IsEqualTo(size + 4);
			Check.That(implementation1.InstanceClass.Nodes.Count).IsEqualTo(4);
			Check.That(tail1.Offset).IsEqualTo(size + 8);
			Check.That(tail2.Offset).IsEqualTo(size + 4);

			Check.That(StructureLayout.CanResizeClass(template, template.MemorySize + 4, project.Classes, out var reason)).IsFalse();
			Check.That(reason).Contains("Owner2");
		}

		[Fact]
		public void GrowingArgumentClassIsCheckedThroughImplementation()
		{
			var element = CreateClass("Element", new Hex32Node());
			var template = CreateTemplate("TBox", new UInt32Node(), Parameter("T"));
			var implementation = Implement(template, ("T", Instance(element)));
			var tail = new Int32Node();
			CreateClass("Owner", implementation, new Hex32Node(), tail);

			Check.That(StructureLayout.CanResizeClass(element, 8, project.Classes, out _)).IsTrue();
			Check.That(StructureLayout.CanResizeClass(element, 12, project.Classes, out _)).IsFalse();

			element.AddBytes(4);

			Check.That(implementation.MemorySize).IsEqualTo(12);
			Check.That(implementation.InstanceClass.MemorySize).IsEqualTo(12);
			Check.That(tail.Offset).IsEqualTo(12);
		}

		[Fact]
		public void TemplateCanPassParametersToOtherTemplate()
		{
			var skillbar = CreateClass("Skillbar", new Hex64Node());
			var arrayTemplate = CreateArrayTemplate();

			var inner = new TemplateImplementationNode { Name = "slots" };
			inner.SetTemplate(arrayTemplate, new[] { new KeyValuePair<string, BaseNode>("T", Parameter("K")) });
			var hashTemplate = CreateTemplate("THash", new UInt32Node { Name = "mask" }, inner);
			inner.Rebuild(project);

			Check.That(hashTemplate.Parameters).ContainsExactly("K");
			Check.That(inner.TypeName).IsEqualTo("TArray<K>");

			var implementation = Implement(hashTemplate, ("K", Instance(skillbar)));

			Check.That(implementation.TypeName).IsEqualTo("THash<Skillbar>");
			var nested = (TemplateImplementationNode)implementation.InstanceClass.Nodes[1];
			Check.That(nested.TypeName).IsEqualTo("TArray<Skillbar>");
			Check.That(nested.Name).IsEqualTo("slots");
			var buffer = (ArrayBufferNode)nested.InstanceClass.Nodes[0];
			Check.That(((ClassInstanceNode)buffer.InnerNode).InnerNode).IsSameReferenceAs(skillbar);
			Check.That(implementation.MemorySize).IsEqualTo(4 + IntPtr.Size + 8);

			// A change of the inner template reaches the implementations of the outer template.
			CreateClass("Owner", implementation, new Hex64Node());
			arrayTemplate.AddBytes(4);

			Check.That(implementation.MemorySize).IsEqualTo(4 + IntPtr.Size + 12);
		}

		[Fact]
		public void CyclesAreDetected()
		{
			var arrayTemplate = CreateArrayTemplate();
			var boxTemplate = CreateTemplate("TBox", Parameter("T"));
			var classNode = CreateClass("Node", new Hex64Node());

			var self = new[] { new KeyValuePair<string, BaseNode>("T", Instance(classNode)) };

			// Behind a buffer pointer a class can contain itself.
			Check.That(ClassUtil.IsCyclicIfTemplateIsImplemented(classNode, arrayTemplate, self, project.Classes)).IsFalse();
			// Inline it can't.
			Check.That(ClassUtil.IsCyclicIfTemplateIsImplemented(classNode, boxTemplate, self, project.Classes)).IsTrue();
			// A template can't implement itself.
			Check.That(ClassUtil.IsCyclicIfTemplateIsImplemented(boxTemplate, boxTemplate, null, project.Classes)).IsTrue();
			Check.That(ClassUtil.IsCyclicIfTemplateIsImplemented(arrayTemplate, boxTemplate, null, project.Classes)).IsFalse();
		}

		[Fact]
		public void ExistingImplementationsTakePartInCycleCheck()
		{
			var boxTemplate = CreateTemplate("TBox", Parameter("T"));
			var inner = CreateClass("Inner", new Hex64Node());
			var outer = CreateClass("Outer", Implement(boxTemplate, ("T", Instance(inner))));

			Check.That(ClassUtil.IsCyclicIfClassIsAccessibleFromParent(inner, outer, project.Classes)).IsTrue();
			Check.That(ClassUtil.IsCyclicIfClassIsAccessibleFromParent(outer, inner, project.Classes)).IsFalse();
		}

		[Fact]
		public void UsedTemplatesAndArgumentClassesCanNotBeRemoved()
		{
			var template = CreateArrayTemplate();
			var element = CreateClass("Element", new Hex32Node());
			var unused = CreateTemplate("Unused");
			CreateClass("Owner", Implement(template, ("T", Instance(element))));

			Check.ThatCode(() => project.RemoveTemplate(template)).Throws<TemplateReferencedException>();
			Check.ThatCode(() => project.Remove(element)).Throws<ClassReferencedException>();

			project.RemoveTemplate(unused);

			Check.That(project.Templates).ContainsExactly(template);
		}

		[Fact]
		public void SaveAndLoadKeepsTemplates()
		{
			var element = CreateClass("Element", new Hex32Node(), new Hex32Node());
			var arrayTemplate = CreateArrayTemplate();
			var inner = new TemplateImplementationNode { Name = "slots" };
			inner.SetTemplate(arrayTemplate, new[] { new KeyValuePair<string, BaseNode>("T", Parameter("K")) });
			var hashTemplate = CreateTemplate("THash", inner, Parameter("K", "first"));
			inner.Rebuild(project);
			var tail = new Int32Node { Name = "tail" };
			var owner = CreateClass("Owner", Implement(hashTemplate, ("K", Instance(element))), tail);
			owner.Nodes[0].Name = "hash";

			byte[] data;
			using (var output = new MemoryStream())
			{
				new ReClassNetFile(project).Save(output, new NullLogger());
				data = output.ToArray();
			}

			var loadedProject = new ReClassNetProject();
			using (var input = new MemoryStream(data))
			{
				new ReClassNetFile(loadedProject).Load(input, new NullLogger());
			}

			Check.That(loadedProject.Templates.Select(t => t.DisplayName)).ContainsExactly("TArray<T>", "THash<K>");
			Check.That(loadedProject.Classes.Select(c => c.Name)).ContainsExactly("Element", "Owner");

			var loadedOwner = loadedProject.Classes[1];
			Check.That(loadedOwner.MemorySize).IsEqualTo(owner.MemorySize);
			Check.That(loadedOwner.Nodes[1].Offset).IsEqualTo(tail.Offset);

			var loaded = (TemplateImplementationNode)loadedOwner.Nodes[0];
			Check.That(loaded.Name).IsEqualTo("hash");
			Check.That(loaded.TypeName).IsEqualTo("THash<Element>");
			Check.That(loaded.Template).IsSameReferenceAs(loadedProject.Templates[1]);
			Check.That(loaded.InstanceClass.Nodes.Count).IsEqualTo(2);
			Check.That(((TemplateImplementationNode)loaded.InstanceClass.Nodes[0]).TypeName).IsEqualTo("TArray<Element>");

			// The loaded project still follows changes of the class.
			loadedOwner.AddBytes(8);
			var before = loadedOwner.Nodes[1].Offset;
			loadedProject.Classes[0].AddBytes(4);
			Check.That(loaded.MemorySize).IsEqualTo(owner.Nodes[0].MemorySize + 4);
			Check.That(loadedOwner.Nodes[1].Offset).IsEqualTo(before + 4);
		}

		[Fact]
		public void CppCodeUsesTemplates()
		{
			var skillbar = CreateClass("Skillbar", new Int32Node { Name = "id" });
			var arrayTemplate = CreateArrayTemplate();
			var boxTemplate = CreateTemplate("TBox", Parameter("T"), new UInt32Node { Name = "tail" });
			var owner = CreateClass(
				"Owner",
				new Int32Node { Name = "first" },
				Implement(arrayTemplate, ("T", Instance(skillbar))),
				Implement(boxTemplate, ("T", new UInt8Node())),
				Wrap<PointerNode>(Implement(boxTemplate, ("T", Wrap<PointerNode>(Instance(skillbar)))), "boxPtr")
			);
			owner.Nodes[1].Name = "skills";
			owner.Nodes[2].Name = "box";

			var code = new CppCodeGenerator(project.TypeMapping).GenerateCode(new[] { owner }, Array.Empty<EnumDescription>(), new NullLogger());

			Check.That(code).Contains("template<typename T>");
			Check.That(code).Contains("class TArray");
			Check.That(code).Contains("T *data;");
			Check.That(code).Contains("T value;");
			Check.That(code).Contains("TArray<class Skillbar> skills;");
			Check.That(code).Contains("TBox<uint8_t> box;");
			Check.That(code).Contains("TBox<class Skillbar*> *boxPtr;");
			Check.That(code).DoesNotContain("sizeof(TArray)");
			Check.That(code).Contains("static_assert(sizeof(Owner)");
			Check.That(code.IndexOf("class TBox", StringComparison.Ordinal)).IsStrictlyLessThan(code.IndexOf("class Owner", StringComparison.Ordinal));
		}

		[Fact]
		public void CSharpCodeWritesImplementationsAsStructs()
		{
			var boxTemplate = CreateTemplate("TBox", Parameter("T"), new UInt32Node { Name = "tail" });
			var owner = CreateClass("Owner", Implement(boxTemplate, ("T", new UInt8Node())));
			owner.Nodes[0].Name = "box";

			var code = new CSharpCodeGenerator().GenerateCode(new[] { owner }, Array.Empty<EnumDescription>(), new NullLogger());

			Check.That(code).Contains("public readonly TBox_UInt8 box;");
			Check.That(code).Contains("public struct TBox_UInt8");
		}

		/// <summary>TLink&lt;T&gt; { TLink&lt;T&gt;* m_prevLink; T* m_nextNode; }</summary>
		private TemplateNode CreateLinkTemplate()
		{
			var template = CreateTemplate("TLink");

			var prevLink = new TemplateImplementationNode();
			prevLink.SetTemplate(template, new[] { new KeyValuePair<string, BaseNode>("T", Parameter("T")) });

			template.AddNodes(new BaseNode[] { Wrap<PointerNode>(prevLink, "m_prevLink"), Wrap<PointerNode>(Parameter("T"), "m_nextNode") });

			return template;
		}

		[Fact]
		public void TemplateCanPointToItselfAndToItsParameter()
		{
			var linkTemplate = CreateLinkTemplate();
			var node = CreateClass("Node", new Int32Node { Name = "id" });

			Check.That(linkTemplate.Parameters).ContainsExactly("T");
			Check.That(linkTemplate.MemorySize).IsEqualTo(2 * IntPtr.Size);

			var link = Implement(linkTemplate, ("T", Instance(node)));
			node.AddNode(link);

			Check.That(link.TypeName).IsEqualTo("TLink<Node>");
			Check.That(link.MemorySize).IsEqualTo(2 * IntPtr.Size);

			var nextNode = (PointerNode)link.InstanceClass.Nodes[1];
			Check.That(nextNode.Name).IsEqualTo("m_nextNode");
			Check.That(((ClassInstanceNode)nextNode.InnerNode).InnerNode).IsSameReferenceAs(node);

			// The link behind the pointer is only generated when it is used.
			var prevLink = (TemplateImplementationNode)((PointerNode)link.InstanceClass.Nodes[0]).InnerNode;
			Check.That(prevLink.IsBehindPointer).IsTrue();
			Check.That(prevLink.TypeName).IsEqualTo("TLink<Node>");
			Check.That(prevLink.InstanceClass.Nodes.Count).IsEqualTo(0);
			Check.That(prevLink.MemorySize).IsEqualTo(2 * IntPtr.Size);
			Check.That(prevLink.InstanceClass.Nodes.Count).IsEqualTo(2);

			var prevPrevLink = (TemplateImplementationNode)((PointerNode)prevLink.InstanceClass.Nodes[0]).InnerNode;
			Check.That(prevPrevLink.InstanceClass.Nodes.Count).IsEqualTo(0);

			// A change of the template reaches the generated links.
			linkTemplate.AddBytes(4);
			Check.That(link.MemorySize).IsEqualTo(2 * IntPtr.Size + 4);
			prevLink = (TemplateImplementationNode)((PointerNode)link.InstanceClass.Nodes[0]).InnerNode;
			Check.That(prevLink.MemorySize).IsEqualTo(2 * IntPtr.Size + 4);
		}

		[Fact]
		public void ListTemplateEmbedsLinkOfItsParameter()
		{
			var linkTemplate = CreateLinkTemplate();

			var term = new TemplateImplementationNode { Name = "m_term" };
			term.SetTemplate(linkTemplate, new[] { new KeyValuePair<string, BaseNode>("T", Parameter("T")) });
			var listTemplate = CreateTemplate("TList", new UInt32Node { Name = "m_linkOffset" }, term);
			term.Rebuild(project);

			var node = CreateClass("Node", new Int32Node { Name = "id" });
			var arguments = new[] { new KeyValuePair<string, BaseNode>("T", Instance(node)) };

			// The list only points to its nodes, so a node can contain a list of nodes.
			Check.That(ClassUtil.IsCyclicIfTemplateIsImplemented(node, listTemplate, arguments, project.Classes)).IsFalse();

			var list = Implement(listTemplate, ("T", Instance(node)));
			node.AddNode(list);

			Check.That(list.TypeName).IsEqualTo("TList<Node>");
			Check.That(list.MemorySize).IsEqualTo(4 + 2 * IntPtr.Size);
			var listTerm = (TemplateImplementationNode)list.InstanceClass.Nodes[1];
			Check.That(listTerm.TypeName).IsEqualTo("TLink<Node>");
			Check.That(listTerm.InstanceClass.Nodes.Count).IsEqualTo(2);

			byte[] data;
			using (var output = new MemoryStream())
			{
				new ReClassNetFile(project).Save(output, new NullLogger());
				data = output.ToArray();
			}

			var loadedProject = new ReClassNetProject();
			using (var input = new MemoryStream(data))
			{
				new ReClassNetFile(loadedProject).Load(input, new NullLogger());
			}

			var loadedNode = loadedProject.Classes[0];
			Check.That(loadedNode.MemorySize).IsEqualTo(node.MemorySize);
			var loadedList = (TemplateImplementationNode)loadedNode.Nodes[1];
			Check.That(loadedList.TypeName).IsEqualTo("TList<Node>");
			var loadedTerm = (TemplateImplementationNode)loadedList.InstanceClass.Nodes[1];
			var loadedPrev = (TemplateImplementationNode)((PointerNode)loadedTerm.InstanceClass.Nodes[0]).InnerNode;
			Check.That(loadedPrev.MemorySize).IsEqualTo(2 * IntPtr.Size);

			var code = new CppCodeGenerator(loadedProject.TypeMapping).GenerateCode(loadedProject.Classes, Array.Empty<EnumDescription>(), new NullLogger());
			Check.That(code).Contains("TLink<T> *m_prevLink;");
			Check.That(code).Contains("T *m_nextNode;");
			Check.That(code).Contains("TLink<T> m_term;");
			Check.That(code).Contains("TList<class Node>");
		}
	}
}
