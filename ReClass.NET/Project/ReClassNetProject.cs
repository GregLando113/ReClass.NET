using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using ReClassNET.Nodes;
using ReClassNET.Util;

namespace ReClassNET.Project
{
	public class ReClassNetProject : IDisposable
	{
		public delegate void ClassesChangedEvent(ClassNode sender);
		public event ClassesChangedEvent ClassAdded;
		public event ClassesChangedEvent ClassRemoved;

		public delegate void EnumsChangedEvent(EnumDescription sender);
		public event EnumsChangedEvent EnumAdded;
		public event EnumsChangedEvent EnumRemoved;

		private readonly List<EnumDescription> enums = new List<EnumDescription>();
		private readonly List<ClassNode> classes = new List<ClassNode>();
		private readonly List<TemplateNode> templates = new List<TemplateNode>();

		public delegate void TemplatesChangedEvent(TemplateNode sender);
		public event TemplatesChangedEvent TemplateAdded;
		public event TemplatesChangedEvent TemplateRemoved;

		/// <summary>The class templates of the project.</summary>
		public IReadOnlyList<TemplateNode> Templates => templates;

		private IEnumerable<ClassNode> ClassesAndTemplates => classes.Concat(templates);

		public IReadOnlyList<EnumDescription> Enums => enums;

		public IReadOnlyList<ClassNode> Classes => classes;

		public string Path { get; set; }

		/// <summary>
		/// Key-Value map with custom data for plugins to store project related data.
		/// The preferred key format is {Plugin Name}_{Key Name}.
		/// </summary>
		public CustomDataMap CustomData { get; } = new CustomDataMap();

		/// <summary>
		/// List of data types to use while generating C++ code for nodes.
		/// </summary>
		public CppTypeMapping TypeMapping { get; } = new CppTypeMapping();

		public void Dispose()
		{
			Clear();

			ClassAdded = null;
			ClassRemoved = null;

			TemplateAdded = null;
			TemplateRemoved = null;

			EnumAdded = null;
			EnumRemoved = null;
		}

		public void AddClass(ClassNode node)
		{
			Contract.Requires(node != null);

			classes.Add(node);

			node.NodesChanged += NodesChanged_Handler;

			ClassAdded?.Invoke(node);
		}

		public bool ContainsClass(Guid uuid)
		{
			Contract.Requires(uuid != null);

			return classes.Any(c => c.Uuid.Equals(uuid));
		}

		public ClassNode GetClassByUuid(Guid uuid)
		{
			Contract.Requires(uuid != null);

			return classes.First(c => c.Uuid.Equals(uuid));
		}

		private int bulkLoadCount;
		private bool isReconciling;

		/// <summary>
		/// Disables the layout reconciliation while classes get loaded (classes may be referenced before their nodes are added).
		/// <see cref="EndBulkLoad"/> must be called afterwards.
		/// </summary>
		public void BeginBulkLoad()
		{
			bulkLoadCount++;
		}

		/// <summary>
		/// Enables the layout reconciliation disabled by <see cref="BeginBulkLoad"/> and accepts the current layout of all classes as is.
		/// </summary>
		public void EndBulkLoad()
		{
			bulkLoadCount = Math.Max(0, bulkLoadCount - 1);
			if (bulkLoadCount == 0)
			{
				templates.ForEach(t => t.UpdateParameters());

				// The templates may not have been complete when their implementations were loaded.
				foreach (var implementation in TemplateUtil.EnumerateImplementations(ClassesAndTemplates, false).Where(i => i.NeedsRebuild).ToList())
				{
					implementation.Rebuild(this, false);
				}

				foreach (var classNode in ClassesAndTemplates)
				{
					classNode.ResetLayoutBaseline();
				}

				UpdateGeneratedClassOffsets();
			}
		}

		/// <summary>True while classes get loaded (see <see cref="BeginBulkLoad"/>).</summary>
		public bool IsBulkLoading => bulkLoadCount > 0;

		public void AddTemplate(TemplateNode template)
		{
			Contract.Requires(template != null);

			templates.Add(template);

			template.UpdateParameters();

			template.NodesChanged += TemplateNodesChanged_Handler;

			TemplateAdded?.Invoke(template);
		}

		public bool ContainsTemplate(Guid uuid)
		{
			return templates.Any(t => t.Uuid.Equals(uuid));
		}

		public TemplateNode GetTemplateByUuid(Guid uuid)
		{
			return templates.FirstOrDefault(t => t.Uuid.Equals(uuid));
		}

		/// <summary>Gets the classes and templates which use the template, including uses as template argument.</summary>
		public IEnumerable<ClassNode> GetTemplateReferences(TemplateNode template)
		{
			Contract.Requires(template != null);

			return ClassesAndTemplates
				.Where(c => c != template)
				.Where(c => TemplateUtil.Flatten(c.Nodes, false, true).OfType<TemplateImplementationNode>().Any(i => i.Template == template));
		}

		public void RemoveTemplate(TemplateNode template)
		{
			Contract.Requires(template != null);

			var references = GetTemplateReferences(template).ToList();
			if (references.Any())
			{
				throw new TemplateReferencedException(template, references);
			}

			if (templates.Remove(template))
			{
				template.NodesChanged -= TemplateNodesChanged_Handler;

				TemplateRemoved?.Invoke(template);
			}
		}

		/// <summary>
		/// Regenerates all implementations which use the template. If such an implementation is part of another template,
		/// the implementations of that template get regenerated too.
		/// </summary>
		private void RebuildImplementations(TemplateNode template, HashSet<TemplateNode> visited)
		{
			if (!visited.Add(template))
			{
				return;
			}

			bool UsesTemplate(TemplateImplementationNode implementation)
			{
				return implementation.Template == template
					|| TemplateUtil.Flatten(implementation.Arguments.Values, false, true).OfType<TemplateImplementationNode>().Any(i => i.Template == template);
			}

			var implementations = TemplateUtil.EnumerateImplementations(ClassesAndTemplates, false)
				.Where(UsesTemplate)
				.ToList();

			foreach (var implementation in implementations)
			{
				implementation.Rebuild(this, false);
			}

			foreach (var parentTemplate in implementations.Select(i => i.GetParentClass()).OfType<TemplateNode>().Distinct().ToList())
			{
				parentTemplate.UpdateParameters();

				RebuildImplementations(parentTemplate, visited);
			}
		}

		private void UpdateGeneratedClassOffsets()
		{
			foreach (var implementation in TemplateUtil.EnumerateImplementations(ClassesAndTemplates, true))
			{
				implementation.InstanceClass.UpdateOffsets();
			}
		}

		private void TemplateNodesChanged_Handler(BaseNode sender)
		{
			if (sender is TemplateNode template)
			{
				template.UpdateParameters();

				if (bulkLoadCount == 0 && !isReconciling)
				{
					RebuildImplementations(template, new HashSet<TemplateNode>());
				}
			}

			NodesChanged_Handler(sender);
		}

		private void NodesChanged_Handler(BaseNode sender)
		{
			if (isReconciling)
			{
				return;
			}

			if (bulkLoadCount > 0)
			{
				foreach (var classNode in ClassesAndTemplates)
				{
					classNode.UpdateOffsets();
				}

				UpdateGeneratedClassOffsets();

				return;
			}

			isReconciling = true;
			try
			{
				// If a class changed its size, every class which embeds it has to absorb or pad the difference.
				// Repeat until the layout is stable. Class cycles are not possible, so this terminates.
				var all = ClassesAndTemplates.ToList();
				for (var pass = 0; pass <= all.Count; ++pass)
				{
					var changed = false;
					foreach (var classNode in all)
					{
						if (classNode.ReconcileChildSizes())
						{
							changed = true;

							// The implementations are copies of the template and have to follow the change.
							if (classNode is TemplateNode template)
							{
								RebuildImplementations(template, new HashSet<TemplateNode>());
							}
						}
					}

					if (!changed)
					{
						break;
					}
				}

				UpdateGeneratedClassOffsets();
			}
			finally
			{
				isReconciling = false;
			}
		}

		public void Clear()
		{
			var temp = classes.ToList();

			classes.Clear();

			foreach (var node in temp)
			{
				node.NodesChanged -= NodesChanged_Handler;

				ClassRemoved?.Invoke(node);
			}

			var temp3 = templates.ToList();

			templates.Clear();

			foreach (var template in temp3)
			{
				template.NodesChanged -= TemplateNodesChanged_Handler;

				TemplateRemoved?.Invoke(template);
			}

			var temp2 = enums.ToList();

			enums.Clear();

			foreach (var @enum in temp2)
			{
				EnumRemoved?.Invoke(@enum);
			}
		}

		private IEnumerable<ClassNode> GetClassReferences(ClassNode node)
		{
			Contract.Requires(node != null);

			return ClassesAndTemplates
				.Where(c => c != node)
				.Where(c => TemplateUtil.ReferencesClass(c.Nodes, node));
		}

		public void Remove(ClassNode node)
		{
			Contract.Requires(node != null);

			var references = GetClassReferences(node).ToList();
			if (references.Any())
			{
				throw new ClassReferencedException(node, references);
			}

			if (classes.Remove(node))
			{
				node.NodesChanged -= NodesChanged_Handler;

				ClassRemoved?.Invoke(node);
			}
		}

		public void RemoveUnusedClasses()
		{
			var toRemove = classes
				.Except(classes.Where(x => GetClassReferences(x).Any())) // check for references
				.Where(c => c.Nodes.All(n => n is BaseHexNode)) // check if only hex nodes are present
				.ToList();
			foreach (var node in toRemove)
			{
				if (classes.Remove(node))
				{
					ClassRemoved?.Invoke(node);
				}
			}
		}

		public void AddEnum(EnumDescription @enum)
		{
			Contract.Requires(@enum != null);

			enums.Add(@enum);

			EnumAdded?.Invoke(@enum);
		}

		public void RemoveEnum(EnumDescription @enum)
		{
			Contract.Requires(@enum != null);

			var refrences = GetEnumReferences(@enum).ToList();
			if (refrences.Any())
			{
				throw new EnumReferencedException(@enum, refrences.Select(e => e.GetParentClass()).Distinct());
			}

			if (enums.Remove(@enum))
			{
				EnumRemoved?.Invoke(@enum);
			}
		}

		private IEnumerable<EnumNode> GetEnumReferences(EnumDescription @enum)
		{
			Contract.Requires(@enum != null);

			return classes
				.SelectMany(c => c.Nodes.Where(n => n is EnumNode || (n as BaseWrapperNode)?.ResolveMostInnerNode() is EnumNode))
				.Cast<EnumNode>()
				.Where(e => e.Enum == @enum);
		}
	}

	public class ClassReferencedException : Exception
	{
		public ClassNode ClassNode { get; }
		public IEnumerable<ClassNode> References { get; }

		public ClassReferencedException(ClassNode node, IEnumerable<ClassNode> references)
			: base($"The class '{node.Name}' is referenced in other classes.")
		{
			Contract.Requires(node != null);
			Contract.Requires(references != null);
			Contract.Requires(Contract.ForAll(references, c => c != null));

			ClassNode = node;
			References = references;
		}
	}

	public class TemplateReferencedException : Exception
	{
		public TemplateNode Template { get; }
		public IEnumerable<ClassNode> References { get; }

		public TemplateReferencedException(TemplateNode template, IEnumerable<ClassNode> references)
			: base($"The template '{template.Name}' is used in other classes.")
		{
			Template = template;
			References = references;
		}
	}

	public class EnumReferencedException : Exception
	{
		public EnumDescription Enum { get; }
		public IEnumerable<ClassNode> References { get; }

		public EnumReferencedException(EnumDescription @enum, IEnumerable<ClassNode> references)
			: base($"The enum '{@enum.Name}' is referenced in other classes.")
		{
			Contract.Requires(@enum != null);
			Contract.Requires(references != null);
			Contract.Requires(Contract.ForAll(references, c => c != null));

			Enum = @enum;
			References = references;
		}
	}
}
