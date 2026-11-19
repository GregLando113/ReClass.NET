using System;
using System.Collections.Generic;
using System.Linq;
using ReClassNET.Extensions;
using ReClassNET.Nodes;

namespace ReClassNET.CodeGenerator
{
	internal static class CodeGeneratorUtil
	{
		/// <summary>
		/// Gets the classes to write for the given classes in an order where every class is defined before it is used.
		/// The result also contains the templates and embedded classes the given classes depend on, even if they are not part of the list.
		/// Classes which contain a <see cref="FunctionNode"/> are skipped because these are not data classes.
		/// </summary>
		public static List<ClassNode> OrderClassesByDependencies(IEnumerable<ClassNode> classes)
		{
			// First collect everything to write with the argument classes in front of their users where possible,
			// then order by the classes which really have to be defined first.
			return OrderByDependencies(
				OrderByDependencies(
					classes.Where(c => c.Nodes.None(n => n is FunctionNode)),
					GetRequiredAndArgumentClasses
				),
				GetRequiredClasses
			);
		}

		/// <summary>Gets the classes and templates which have to be defined before the class.</summary>
		private static List<ClassNode> GetRequiredClasses(ClassNode node)
		{
			// A template has to be defined before it is used, even if it is only used behind a pointer.
			var templateNodes = TemplateUtil.Flatten(node.Nodes, false, true)
				.OfType<TemplateImplementationNode>()
				.Select(i => (ClassNode)i.Template)
				.Where(t => t != null);

			// Embedded classes have to be defined first. Pointers are forward declared.
			return templateNodes
				.Concat(ClassUtil.GetEmbeddedClasses(node.Nodes))
				.Where(c => !(c is TemplateInstanceClassNode))
				.Distinct()
				.ToList();
		}

		// The classes used as template argument (TArray<Skillbar>) belong to the class too. If the template only points to
		// them they don't need to be defined first, so they can't be allowed to break the order of the required classes.
		private static List<ClassNode> GetRequiredAndArgumentClasses(ClassNode node)
		{
			return GetRequiredClasses(node)
				.Concat(TemplateUtil.GetArgumentClasses(node.Nodes))
				.Distinct()
				.ToList();
		}

		private static List<ClassNode> OrderByDependencies(IEnumerable<ClassNode> roots, Func<ClassNode, List<ClassNode>> getDependencies)
		{
			var alreadySeen = new HashSet<ClassNode>();
			var ordered = new List<ClassNode>();

			void Visit(ClassNode node)
			{
				if (!alreadySeen.Add(node))
				{
					return;
				}

				getDependencies(node).ForEach(Visit);

				ordered.Add(node);
			}

			foreach (var root in roots)
			{
				Visit(root);
			}

			return ordered;
		}
	}
}
