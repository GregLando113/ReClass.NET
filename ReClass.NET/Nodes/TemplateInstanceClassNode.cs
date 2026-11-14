using System.Collections.Generic;

namespace ReClassNET.Nodes
{
	/// <summary>
	/// The class generated for a <see cref="TemplateImplementationNode"/>: a copy of the template's nodes with the
	/// template parameters replaced by the arguments. It is read-only and not part of the project's class list.
	/// </summary>
	public class TemplateInstanceClassNode : ClassNode
	{
		/// <summary>The implementation node this class was generated for.</summary>
		public TemplateImplementationNode Owner { get; }

		public override bool IsReadOnly => true;

		// Unset arguments stay placeholders.
		public override bool AllowsTemplateParameters => true;

		/// <summary>The name is derived from the template and its arguments, e.g. TArray&lt;Skillbar&gt;.</summary>
		public override string Name
		{
			get => Owner?.TypeName ?? string.Empty;
			set { }
		}

		internal TemplateInstanceClassNode(TemplateImplementationNode owner)
			: base(false)
		{
			Owner = owner;
		}

		/// <summary>Replaces all nodes of the class.</summary>
		internal void SetNodes(IEnumerable<BaseNode> nodes)
		{
			BeginUpdate();
			ClearNodes();
			AddNodes(nodes);
			EndUpdate();
		}

		protected override void NotifyNodesUpdated()
		{
			// The owner informs its container after a rebuild.
		}
	}
}
