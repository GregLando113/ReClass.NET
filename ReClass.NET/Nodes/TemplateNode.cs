using System.Collections.Generic;
using System.Linq;

namespace ReClassNET.Nodes
{
	/// <summary>
	/// A class template. Its layout can contain <see cref="TemplateParameterNode"/> placeholders which get replaced
	/// with real types by a <see cref="TemplateImplementationNode"/>.
	/// </summary>
	public class TemplateNode : ClassNode
	{
		private List<string> parameters = new List<string>();

		/// <summary>The names of the template parameters in the order of their first use.</summary>
		public IReadOnlyList<string> Parameters => parameters;

		/// <summary>The name with the parameter list, e.g. TArray&lt;T&gt;.</summary>
		public string DisplayName => parameters.Count == 0 ? Name : $"{Name}<{string.Join(", ", parameters)}>";

		public override bool AllowsTemplateParameters => true;

		internal TemplateNode()
			: base(false)
		{
		}

		public new static TemplateNode Create() => new TemplateNode();

		/// <summary>
		/// Recalculates the parameter list from the placeholders used in the template.
		/// Existing parameters keep their position, new ones are appended.
		/// </summary>
		/// <returns>True if the parameter list has changed.</returns>
		public bool UpdateParameters()
		{
			var used = TemplateUtil.CollectParameterNames(Nodes);

			var updated = parameters.Where(used.Contains).Concat(used.Except(parameters)).ToList();
			if (updated.SequenceEqual(parameters))
			{
				return false;
			}

			parameters = updated;

			return true;
		}
	}
}
