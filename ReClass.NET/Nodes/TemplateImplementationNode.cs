using System.Collections.Generic;
using System.Drawing;
using ReClassNET.Controls;
using ReClassNET.Project;

namespace ReClassNET.Nodes
{
	/// <summary>
	/// An instance of a <see cref="TemplateNode"/> with its template parameters set to real types, e.g. TArray&lt;Skillbar&gt;.
	/// The inner node is a generated read-only class (<see cref="TemplateInstanceClassNode"/>).
	/// </summary>
	public class TemplateImplementationNode : ClassInstanceNode
	{
		private readonly Dictionary<string, BaseNode> arguments = new Dictionary<string, BaseNode>();

		/// <summary>The implemented template or null if no template is set.</summary>
		public TemplateNode Template { get; private set; }

		/// <summary>The type set for each template parameter. Parameters without an entry stay placeholders.</summary>
		public IReadOnlyDictionary<string, BaseNode> Arguments => arguments;

		/// <summary>The generated class.</summary>
		public TemplateInstanceClassNode InstanceClass => (TemplateInstanceClassNode)InnerNode;

		/// <summary>True if the nodes of the generated class were not generated yet.</summary>
		internal bool NeedsRebuild { get; private set; }

		private ReClassNetProject pendingProject;

		/// <summary>
		/// True if the implementation is the target of a pointer or array buffer and is not embedded in its class.
		/// </summary>
		public bool IsBehindPointer
		{
			get
			{
				for (var parent = ParentNode; parent is BaseWrapperNode; parent = parent.ParentNode)
				{
					if (parent is PointerNode || parent is ArrayBufferNode)
					{
						return true;
					}
				}

				return false;
			}
		}

		public override int MemorySize
		{
			get
			{
				EnsureBuilt();

				return base.MemorySize;
			}
		}

		/// <summary>
		/// Marks the node to be generated later: at the end of a bulk load or, for implementations behind a pointer,
		/// when they are used for the first time. This is what allows a template to point to itself
		/// (TLink&lt;T&gt; { TLink&lt;T&gt;* prev; }) without generating an endless chain.
		/// </summary>
		internal void RebuildLater(ReClassNetProject project)
		{
			pendingProject = project;

			NeedsRebuild = true;
		}

		/// <summary>Generates the nodes if this was postponed by <see cref="RebuildLater"/>.</summary>
		internal void EnsureBuilt()
		{
			if (NeedsRebuild && pendingProject != null && !pendingProject.IsBulkLoading)
			{
				Rebuild(pendingProject, false);
			}
		}

		public override Size Draw(DrawContext context, int x, int y)
		{
			EnsureBuilt();

			return base.Draw(context, x, y);
		}

		public override int CalculateDrawnHeight(DrawContext context)
		{
			EnsureBuilt();

			return base.CalculateDrawnHeight(context);
		}

		/// <summary>The name of the implemented type, e.g. TArray&lt;Skillbar&gt;.</summary>
		public string TypeName => TemplateUtil.GetTypeName(this);

		public TemplateImplementationNode()
		{
			ChangeInnerNode(new TemplateInstanceClassNode(this));
		}

		public override void Initialize()
		{
			// The generated class already exists. Don't create a new project class like a class instance does.
		}

		public override bool CanChangeInnerNodeTo(BaseNode node)
		{
			return node is TemplateInstanceClassNode instanceClass && instanceClass.Owner == this;
		}

		public override void GetUserInterfaceInfo(out string name, out Image icon)
		{
			name = "Template Implementation";
			icon = Properties.Resources.B16x16_Button_Class_Add;
		}

		/// <summary>
		/// Sets the template and its arguments. <see cref="Rebuild"/> must be called afterwards.
		/// </summary>
		/// <param name="template">The template or null.</param>
		/// <param name="newArguments">The type for each template parameter or null.</param>
		public void SetTemplate(TemplateNode template, IEnumerable<KeyValuePair<string, BaseNode>> newArguments)
		{
			Template = template;

			var copy = new List<KeyValuePair<string, BaseNode>>();
			if (newArguments != null)
			{
				copy.AddRange(newArguments);
			}

			arguments.Clear();
			foreach (var kv in copy)
			{
				if (kv.Key != null && kv.Value != null)
				{
					arguments[kv.Key] = kv.Value;
				}
			}
		}

		/// <summary>
		/// Regenerates the nodes of the generated class from the template and the arguments.
		/// </summary>
		/// <param name="project">The project used to resolve classes, enums and templates.</param>
		/// <param name="notifyParent">True to inform the parent container about the change.</param>
		public void Rebuild(ReClassNetProject project, bool notifyParent = true)
		{
			NeedsRebuild = false;
			pendingProject = null;

			InstanceClass.SetNodes(TemplateInstantiator.CreateNodes(this, project));

			if (notifyParent)
			{
				GetParentContainer()?.ChildHasChanged(this);
			}
		}
	}
}
