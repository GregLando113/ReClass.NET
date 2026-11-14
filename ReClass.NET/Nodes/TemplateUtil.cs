using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using ReClassNET.DataExchange.ReClass;
using ReClassNET.Logger;
using ReClassNET.Project;

namespace ReClassNET.Nodes
{
	public static class TemplateUtil
	{
		/// <summary>Gets the display name of a type used as template argument, e.g. Skillbar, UInt8, Skillbar* or TArray&lt;Skillbar&gt;.</summary>
		/// <param name="node">The argument node or null if the argument is not set.</param>
		public static string GetTypeName(BaseNode node)
		{
			switch (node)
			{
				case null:
					return "?";
				case TemplateImplementationNode implementationNode:
				{
					if (implementationNode.Template == null)
					{
						return "?";
					}

					var template = implementationNode.Template;
					var arguments = template.Parameters
						.Select(p => GetTypeName(implementationNode.Arguments.TryGetValue(p, out var argument) ? argument : null));

					return template.Parameters.Count == 0 ? template.Name : $"{template.Name}<{string.Join(", ", arguments)}>";
				}
				case TemplateParameterNode parameterNode:
					return parameterNode.ParameterName;
				case ClassNode classNode:
					return classNode.Name;
				case PointerNode pointerNode:
					return pointerNode.InnerNode == null ? "void*" : GetTypeName(pointerNode.InnerNode) + "*";
				case ArrayBufferNode arrayBufferNode:
					return GetTypeName(arrayBufferNode.InnerNode) + "*";
				case BaseWrapperArrayNode arrayNode:
					return $"{GetTypeName(arrayNode.InnerNode)}[{arrayNode.Count}]";
				case BaseWrapperNode wrapperNode:
					return GetTypeName(wrapperNode.InnerNode);
			}

			var name = node.GetType().Name;
			return name.EndsWith("Node") ? name.Substring(0, name.Length - 4) : name;
		}

		/// <summary>
		/// Enumerates the nodes and all nodes nested in unions, wrapper chains and template arguments.
		/// Classes are not entered, except generated template classes if <paramref name="intoGeneratedClasses"/> is set.
		/// </summary>
		public static IEnumerable<BaseNode> Flatten(IEnumerable<BaseNode> nodes, bool intoGeneratedClasses, bool intoArguments)
		{
			foreach (var node in nodes)
			{
				if (node == null)
				{
					continue;
				}

				yield return node;

				IEnumerable<BaseNode> children = null;
				switch (node)
				{
					case TemplateImplementationNode implementationNode:
						children = Enumerable.Empty<BaseNode>();
						if (intoArguments)
						{
							children = children.Concat(implementationNode.Arguments.Values);
						}
						if (intoGeneratedClasses)
						{
							children = children.Concat(implementationNode.InstanceClass.Nodes);
						}
						break;
					case ClassNode _:
						break;
					case BaseContainerNode containerNode:
						children = containerNode.Nodes;
						break;
					case BaseWrapperNode wrapperNode when wrapperNode.InnerNode != null && !(wrapperNode.InnerNode is ClassNode):
						children = new[] { wrapperNode.InnerNode };
						break;
				}

				if (children != null)
				{
					foreach (var child in Flatten(children, intoGeneratedClasses, intoArguments))
					{
						yield return child;
					}
				}
			}
		}

		/// <summary>Collects the names of all template parameters used by the nodes in the order of their first use.</summary>
		public static List<string> CollectParameterNames(IEnumerable<BaseNode> nodes)
		{
			return Flatten(nodes, false, true)
				.OfType<TemplateParameterNode>()
				.Select(p => p.ParameterName)
				.Distinct()
				.ToList();
		}

		/// <summary>
		/// Enumerates all template implementations in the classes. Implementations which only exist as template argument are not returned.
		/// </summary>
		/// <param name="classes">The classes (and templates) to search.</param>
		/// <param name="intoGeneratedClasses">True to also return the implementations nested in generated classes.</param>
		public static IEnumerable<TemplateImplementationNode> EnumerateImplementations(IEnumerable<ClassNode> classes, bool intoGeneratedClasses)
		{
			return classes
				.SelectMany(c => Flatten(c.Nodes, intoGeneratedClasses, false))
				.OfType<TemplateImplementationNode>();
		}

		/// <summary>
		/// Gets the parameters of the template which are embedded in the memory of the template (not only used behind a pointer).
		/// </summary>
		public static HashSet<string> GetInlineParameters(TemplateNode template)
		{
			return GetInlineParameters(template, new HashSet<TemplateNode>());
		}

		private static HashSet<string> GetInlineParameters(TemplateNode template, HashSet<TemplateNode> visited)
		{
			var result = new HashSet<string>();
			if (template == null || !visited.Add(template))
			{
				return result;
			}

			void Walk(BaseNode node)
			{
				switch (node)
				{
					case null:
					case PointerNode _:
					case ArrayBufferNode _:
						break;
					case TemplateParameterNode parameterNode:
						result.Add(parameterNode.ParameterName);
						break;
					case TemplateImplementationNode implementationNode:
					{
						var inline = GetInlineParameters(implementationNode.Template, visited);
						foreach (var kv in implementationNode.Arguments.Where(kv => inline.Contains(kv.Key)))
						{
							Walk(kv.Value);
						}
						break;
					}
					case ClassNode _:
						break;
					case BaseContainerNode containerNode:
						foreach (var child in containerNode.Nodes)
						{
							Walk(child);
						}
						break;
					case BaseWrapperNode wrapperNode:
						Walk(wrapperNode.InnerNode);
						break;
				}
			}

			foreach (var node in template.Nodes)
			{
				Walk(node);
			}

			visited.Remove(template);

			return result;
		}

		/// <summary>Tests if the class is referenced by the nodes (as class instance, pointer target or template argument).</summary>
		public static bool ReferencesClass(IEnumerable<BaseNode> nodes, ClassNode classNode)
		{
			return Flatten(nodes, false, true)
				.OfType<BaseWrapperNode>()
				.Any(w => w.InnerNode == classNode);
		}
	}

	/// <summary>Creates the nodes of a <see cref="TemplateImplementationNode"/>.</summary>
	public static class TemplateInstantiator
	{
		private const int MaxDepth = 32;

		[System.ThreadStatic]
		private static int depth;

		/// <summary>
		/// Creates a copy of the template's nodes with the template parameters replaced by the arguments of the implementation.
		/// </summary>
		/// <param name="implementation">The implementation.</param>
		/// <param name="project">The project used to resolve classes, enums and templates.</param>
		/// <returns>The new nodes. Empty if no template is set.</returns>
		public static List<BaseNode> CreateNodes(TemplateImplementationNode implementation, ReClassNetProject project)
		{
			var template = implementation.Template;
			if (template == null || project == null || depth >= MaxDepth)
			{
				return new List<BaseNode>();
			}

			depth++;
			try
			{
				var logger = new NullLogger();

				var holder = new XElement(
					"holder",
					template.Nodes.Select(n => ReClassNetFile.CreateElementFromNode(n, logger)).Where(e => e != null)
				);

				// Collect first, the replacements must not be substituted again.
				var placeholders = holder
					.Descendants(ReClassNetFile.XmlNodeElement)
					.Where(e => e.Attribute(ReClassNetFile.XmlTypeAttribute)?.Value == nameof(TemplateParameterNode))
					.ToList();

				foreach (var placeholder in placeholders)
				{
					var parameter = placeholder.Attribute(ReClassNetFile.XmlParameterAttribute)?.Value ?? string.Empty;
					if (!implementation.Arguments.TryGetValue(parameter, out var argument))
					{
						continue;
					}

					var replacement = ReClassNetFile.CreateElementFromNode(argument, logger);
					if (replacement == null)
					{
						continue;
					}

					// The field keeps its name.
					foreach (var attribute in new[] { ReClassNetFile.XmlNameAttribute, ReClassNetFile.XmlCommentAttribute, ReClassNetFile.XmlHiddenAttribute })
					{
						replacement.SetAttributeValue(attribute, placeholder.Attribute(attribute)?.Value);
					}

					placeholder.ReplaceWith(replacement);
				}

				var file = new ReClassNetFile(project);

				return holder
					.Elements()
					.Select(e => file.CreateNodeFromElement(e, implementation.InstanceClass, logger))
					.Where(n => n != null)
					.ToList();
			}
			finally
			{
				depth--;
			}
		}
	}
}
