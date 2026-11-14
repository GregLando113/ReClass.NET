using System.Drawing;
using System.Linq;
using ReClassNET.Controls;
using ReClassNET.UI;

namespace ReClassNET.Nodes
{
	/// <summary>
	/// A placeholder for a template type ("a T goes here"). It has no size and can only be used inside a <see cref="TemplateNode"/>.
	/// A <see cref="TemplateImplementationNode"/> replaces it with a real type.
	/// </summary>
	public class TemplateParameterNode : BaseNode
	{
		/// <summary>The name of the template parameter (e.g. T).</summary>
		public string ParameterName { get; set; } = "T";

		public override int MemorySize => 0;

		public override void GetUserInterfaceInfo(out string name, out Image icon)
		{
			name = "Template Parameter";
			icon = Properties.Resources.B16x16_Custom_Type;
		}

		/// <summary>Tests if the name can be used as template parameter name.</summary>
		public static bool IsValidParameterName(string name)
		{
			return !string.IsNullOrEmpty(name)
				&& (char.IsLetter(name[0]) || name[0] == '_')
				&& name.All(c => char.IsLetterOrDigit(c) || c == '_');
		}

		public override Size Draw(DrawContext context, int x, int y)
		{
			if (IsHidden && !IsWrapped)
			{
				return DrawHidden(context, x, y);
			}

			var origX = x;

			AddSelection(context, x, y, context.Font.Height);

			x = AddIconPadding(context, x);
			x = AddIcon(context, x, y, context.IconProvider.Class, HotSpot.NoneId, HotSpotType.None);
			x = AddAddressOffset(context, x, y);

			x = AddText(context, x, y, context.Settings.TypeColor, 0, ParameterName) + context.Font.Width;
			if (!IsWrapped)
			{
				x = AddText(context, x, y, context.Settings.NameColor, HotSpot.NameId, Name) + context.Font.Width;
			}

			x = AddComment(context, x, y);

			AddContextDropDownIcon(context, y);
			AddDeleteIcon(context, y);

			return new Size(x - origX, context.Font.Height);
		}

		public override int CalculateDrawnHeight(DrawContext context)
		{
			return IsHidden && !IsWrapped ? HiddenHeight : context.Font.Height;
		}

		public override void Update(HotSpot spot)
		{
			base.Update(spot);

			if (spot.Id == 0)
			{
				var name = spot.Text?.Trim();
				if (IsValidParameterName(name) && name != ParameterName && !IsInReadOnlyClass())
				{
					ParameterName = name;

					GetParentContainer()?.ChildHasChanged(this);
				}
			}
		}
	}
}
