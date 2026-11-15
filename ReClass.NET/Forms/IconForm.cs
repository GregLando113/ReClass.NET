using System;
using System.Windows.Forms;
using ReClassNET.UI.Theming;

namespace ReClassNET.Forms
{
	public class IconForm : Form
	{
		public IconForm()
		{
			Icon = Properties.Resources.ReClassNet;
		}

		protected override void OnLoad(EventArgs e)
		{
			DarkMode.AdaptControls(this);

			base.OnLoad(e);
		}
	}
}
