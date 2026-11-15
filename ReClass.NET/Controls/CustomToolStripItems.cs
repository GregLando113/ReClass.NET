using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace ReClassNET.Controls
{
	[ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.All)]
	public class IntegerToolStripMenuItem : ToolStripMenuItem
	{
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public int Value { get; set; }
	}

	[ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.All)]
	public class TypeToolStripMenuItem : ToolStripMenuItem
	{
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public Type Value { get; set; }
	}

	[ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.All)]
	public class TypeToolStripButton : ToolStripButton
	{
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public Type Value { get; set; }
	}
}
