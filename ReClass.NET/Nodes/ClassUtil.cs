using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using ReClassNET.Util;

namespace ReClassNET.Nodes
{
	public class ClassUtil
	{
		/// <summary>
		/// Tests if the class to check can be inserted into the parent class without creating a cycle.
		/// </summary>
		/// <param name="parent">The class into which </param>
		/// <param name="classToCheck">The class which should get inserted.</param>
		/// <param name="classes">An enumeration of all available classes.</param>
		/// <returns>True if a cycle is detected, false otherwise.</returns>
		public static bool IsCyclicIfClassIsAccessibleFromParent(ClassNode parent, ClassNode classToCheck, IEnumerable<ClassNode> classes)
		{
			Contract.Requires(classes != null);

			if (parent == null || classToCheck == null)
			{
				return false;
			}

			var graph = BuildGraph(classes.Append(parent).Append(classToCheck));

			graph.AddEdge(parent, classToCheck);

			return graph.ContainsCycle();
		}

		/// <summary>
		/// Tests if the template with the given arguments can be implemented in the parent class without creating a cycle.
		/// </summary>
		/// <param name="parent">The class (or template) which contains the implementation.</param>
		/// <param name="template">The template to implement.</param>
		/// <param name="arguments">The arguments of the implementation.</param>
		/// <param name="classes">An enumeration of all available classes.</param>
		/// <returns>True if a cycle is detected, false otherwise.</returns>
		public static bool IsCyclicIfTemplateIsImplemented(ClassNode parent, TemplateNode template, IEnumerable<KeyValuePair<string, BaseNode>> arguments, IEnumerable<ClassNode> classes)
		{
			Contract.Requires(classes != null);

			if (parent == null || template == null)
			{
				return false;
			}

			var probe = new TemplateImplementationNode();
			probe.SetTemplate(template, arguments);

			var targets = GetEmbeddedClasses(new BaseNode[] { probe }).ToList();

			var graph = BuildGraph(classes.Append(parent).Concat(targets));

			foreach (var target in targets)
			{
				graph.AddEdge(parent, target);
			}

			return graph.ContainsCycle();
		}

		/// <summary>
		/// Builds the graph of classes (and templates) which embed each other. Classes behind pointers are not connected.
		/// </summary>
		private static DirectedGraph<ClassNode> BuildGraph(IEnumerable<ClassNode> classes)
		{
			var graph = new DirectedGraph<ClassNode>();

			var pending = new Queue<ClassNode>();
			foreach (var c in classes)
			{
				if (c != null && graph.AddVertex(c))
				{
					pending.Enqueue(c);
				}
			}

			while (pending.Count > 0)
			{
				var c = pending.Dequeue();

				foreach (var target in GetEmbeddedClasses(c.Nodes))
				{
					// Templates and generated classes are not part of the class list.
					if (graph.AddVertex(target))
					{
						pending.Enqueue(target);
					}

					graph.AddEdge(c, target);
				}
			}

			return graph;
		}

		/// <summary>
		/// Gets the classes and templates whose memory is embedded by the nodes.
		/// A template implementation embeds its template and the classes of all arguments which are used inline by the template.
		/// </summary>
		internal static IEnumerable<ClassNode> GetEmbeddedClasses(IEnumerable<BaseNode> nodes)
		{
			foreach (var node in nodes)
			{
				switch (node)
				{
					case null:
					case PointerNode _:
					case ArrayBufferNode _:
						break;
					case TemplateImplementationNode implementationNode:
					{
						if (implementationNode.Template == null)
						{
							break;
						}

						yield return implementationNode.Template;

						var inlineParameters = TemplateUtil.GetInlineParameters(implementationNode.Template);
						var inlineArguments = implementationNode.Arguments
							.Where(kv => inlineParameters.Contains(kv.Key))
							.Select(kv => kv.Value);

						foreach (var target in GetEmbeddedClasses(inlineArguments))
						{
							yield return target;
						}
						break;
					}
					case ClassNode classNode:
						yield return classNode;
						break;
					case BaseContainerNode containerNode:
						foreach (var target in GetEmbeddedClasses(containerNode.Nodes))
						{
							yield return target;
						}
						break;
					case BaseWrapperNode wrapperNode:
						if (wrapperNode.ShouldPerformCycleCheckForInnerNode())
						{
							foreach (var target in GetEmbeddedClasses(new[] { wrapperNode.InnerNode }))
							{
								yield return target;
							}
						}
						break;
				}
			}
		}
	}
}
