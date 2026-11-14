using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ReClassNET.Nodes;
using ReClassNET.Project;
using ReClassNET.UI;

namespace ReClassNET.Forms
{
	/// <summary>
	/// Lets the user choose a template and the type of each template parameter for a <see cref="TemplateImplementationNode"/>.
	/// </summary>
	public class TemplateArgumentsForm : IconForm
	{
		private enum ArgumentKind
		{
			Unset,
			Void,
			Parameter,
			Class,
			Template,
			Primitive,
			Custom
		}

		private class KindItem
		{
			public ArgumentKind Kind { get; set; }
			public string Text { get; set; }
			public Type NodeType { get; set; }

			public override string ToString() => Text;
		}

		private class NamedItem<T>
		{
			public T Value { get; set; }
			public string Text { get; set; }

			public override string ToString() => Text;
		}

		/// <summary>The controls and the state of one template parameter.</summary>
		private class ArgumentRow
		{
			public string Parameter { get; set; }
			public ComboBox KindComboBox { get; set; }
			public ComboBox TargetComboBox { get; set; }
			public Button NestedButton { get; set; }
			public CheckBox PointerCheckBox { get; set; }

			public TemplateNode NestedTemplate { get; set; }
			public Dictionary<string, BaseNode> NestedArguments { get; set; } = new Dictionary<string, BaseNode>();
			public BaseNode CustomNode { get; set; }
		}

		private static readonly Type[] primitiveTypes =
		{
			typeof(Hex8Node), typeof(Hex16Node), typeof(Hex32Node), typeof(Hex64Node),
			typeof(Int8Node), typeof(Int16Node), typeof(Int32Node), typeof(Int64Node), typeof(NIntNode),
			typeof(UInt8Node), typeof(UInt16Node), typeof(UInt32Node), typeof(UInt64Node), typeof(NUIntNode),
			typeof(BoolNode), typeof(FloatNode), typeof(DoubleNode),
			typeof(Vector2Node), typeof(Vector3Node), typeof(Vector4Node),
			typeof(Matrix3x3Node), typeof(Matrix3x4Node), typeof(Matrix4x4Node),
			typeof(Utf8TextPtrNode), typeof(Utf16TextPtrNode), typeof(FunctionPtrNode)
		};

		private readonly ReClassNetProject project;
		private readonly bool allowParameters;
		private readonly Dictionary<string, BaseNode> initialArguments;

		private readonly ComboBox templateComboBox;
		private readonly TableLayoutPanel argumentsPanel;
		private readonly Label previewLabel;
		private readonly List<ArgumentRow> rows = new List<ArgumentRow>();

		/// <summary>The chosen template.</summary>
		public TemplateNode SelectedTemplate => (templateComboBox.SelectedItem as NamedItem<TemplateNode>)?.Value;

		/// <param name="project">The project with the available templates and classes.</param>
		/// <param name="allowParameters">True if the implementation is part of a template and can pass its parameters on.</param>
		/// <param name="template">The current template or null.</param>
		/// <param name="arguments">The current arguments or null.</param>
		public TemplateArgumentsForm(ReClassNetProject project, bool allowParameters, TemplateNode template, IEnumerable<KeyValuePair<string, BaseNode>> arguments)
		{
			this.project = project;
			this.allowParameters = allowParameters;

			initialArguments = arguments?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, BaseNode>();

			Text = "Template Implementation";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(560, 120);
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			Padding = new Padding(8);

			var layout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 1
			};
			Controls.Add(layout);

			var templatePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
			templatePanel.Controls.Add(new Label { Text = "Template:", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
			templateComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
			templatePanel.Controls.Add(templateComboBox);
			layout.Controls.Add(templatePanel);

			argumentsPanel = new TableLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 5,
				MinimumSize = new Size(540, 0)
			};
			layout.Controls.Add(argumentsPanel);

			previewLabel = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 8), Font = new Font(Font, FontStyle.Bold) };
			layout.Controls.Add(previewLabel);

			var buttonPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
			var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
			var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK };
			buttonPanel.Controls.Add(cancelButton);
			buttonPanel.Controls.Add(okButton);
			layout.Controls.Add(buttonPanel);

			AcceptButton = okButton;
			CancelButton = cancelButton;

			foreach (var t in project.Templates.OrderBy(t => t.Name))
			{
				templateComboBox.Items.Add(new NamedItem<TemplateNode> { Value = t, Text = t.DisplayName });
			}

			templateComboBox.SelectedIndexChanged += (s, e) => RebuildRows();

			var index = templateComboBox.Items.Cast<NamedItem<TemplateNode>>().ToList().FindIndex(i => i.Value == template);
			templateComboBox.SelectedIndex = index >= 0 ? index : (templateComboBox.Items.Count > 0 ? 0 : -1);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			GlobalWindowManager.AddWindow(this);
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			base.OnFormClosed(e);

			GlobalWindowManager.RemoveWindow(this);
		}

		/// <summary>Creates the argument nodes from the current selection. Parameters which are not set are omitted.</summary>
		public Dictionary<string, BaseNode> CreateArguments()
		{
			var arguments = new Dictionary<string, BaseNode>();
			foreach (var row in rows)
			{
				var node = CreateArgument(row);
				if (node != null)
				{
					arguments[row.Parameter] = node;
				}
			}
			return arguments;
		}

		private void RebuildRows()
		{
			// Keep what was chosen so far if the template is switched.
			var current = rows.Count > 0 ? CreateArguments() : initialArguments;

			rows.Clear();

			argumentsPanel.SuspendLayout();
			argumentsPanel.Controls.Clear();
			argumentsPanel.RowCount = 0;

			var template = SelectedTemplate;
			if (template != null)
			{
				foreach (var parameter in template.Parameters)
				{
					current.TryGetValue(parameter, out var argument);

					AddRow(parameter, argument);
				}

				if (template.Parameters.Count == 0)
				{
					argumentsPanel.Controls.Add(new Label { Text = "This template has no parameters.", AutoSize = true, Margin = new Padding(3, 6, 3, 6) }, 0, 0);
				}
			}

			argumentsPanel.ResumeLayout();

			UpdatePreview();
		}

		private void AddRow(string parameter, BaseNode argument)
		{
			var row = new ArgumentRow
			{
				Parameter = parameter,
				KindComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 },
				TargetComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 },
				NestedButton = new Button { Text = "Choose...", Width = 200 },
				PointerCheckBox = new CheckBox { Text = "Pointer", AutoSize = true, Margin = new Padding(6, 5, 3, 0) }
			};

			row.KindComboBox.Items.Add(new KindItem { Kind = ArgumentKind.Unset, Text = "(not set)" });
			row.KindComboBox.Items.Add(new KindItem { Kind = ArgumentKind.Class, Text = "Class" });
			row.KindComboBox.Items.Add(new KindItem { Kind = ArgumentKind.Template, Text = "Template" });
			if (allowParameters)
			{
				row.KindComboBox.Items.Add(new KindItem { Kind = ArgumentKind.Parameter, Text = "Template parameter" });
			}
			row.KindComboBox.Items.Add(new KindItem { Kind = ArgumentKind.Void, Text = "void (pointer only)" });
			foreach (var type in primitiveTypes)
			{
				row.KindComboBox.Items.Add(new KindItem { Kind = ArgumentKind.Primitive, Text = type.Name.Replace("Node", string.Empty), NodeType = type });
			}

			// Read the current argument.
			var inner = argument;
			if (argument is PointerNode pointerNode)
			{
				row.PointerCheckBox.Checked = true;
				inner = pointerNode.InnerNode;
			}

			KindItem selected;
			string targetText = null;
			ClassNode targetClass = null;
			switch (inner)
			{
				case null:
					selected = FindKind(row, argument is PointerNode ? ArgumentKind.Void : ArgumentKind.Unset);
					break;
				case TemplateImplementationNode implementationNode:
					selected = FindKind(row, ArgumentKind.Template);
					row.NestedTemplate = implementationNode.Template;
					row.NestedArguments = implementationNode.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value);
					break;
				case TemplateParameterNode parameterNode when allowParameters:
					selected = FindKind(row, ArgumentKind.Parameter);
					targetText = parameterNode.ParameterName;
					break;
				case ClassInstanceNode instanceNode when instanceNode.InnerNode is ClassNode classNode && !(classNode is TemplateInstanceClassNode):
					selected = FindKind(row, ArgumentKind.Class);
					targetClass = classNode;
					break;
				default:
					selected = row.KindComboBox.Items.Cast<KindItem>().FirstOrDefault(i => i.Kind == ArgumentKind.Primitive && i.NodeType == inner.GetType());
					if (selected == null)
					{
						// A type this dialog can't build (e.g. an array). It is kept as it is.
						row.CustomNode = inner;
						selected = new KindItem { Kind = ArgumentKind.Custom, Text = TemplateUtil.GetTypeName(inner) };
						row.KindComboBox.Items.Add(selected);
					}
					break;
			}

			row.KindComboBox.SelectedItem = selected;

			UpdateRow(row);

			if (targetClass != null)
			{
				row.TargetComboBox.SelectedItem = row.TargetComboBox.Items.Cast<NamedItem<ClassNode>>().FirstOrDefault(i => i.Value == targetClass);
			}
			else if (targetText != null)
			{
				row.TargetComboBox.Text = targetText;
			}

			row.KindComboBox.SelectedIndexChanged += (s, e) =>
			{
				UpdateRow(row);
				UpdatePreview();
			};
			row.TargetComboBox.SelectedIndexChanged += (s, e) => UpdatePreview();
			row.TargetComboBox.TextChanged += (s, e) => UpdatePreview();
			row.PointerCheckBox.CheckedChanged += (s, e) => UpdatePreview();
			row.NestedButton.Click += (s, e) =>
			{
				using var form = new TemplateArgumentsForm(project, allowParameters, row.NestedTemplate, row.NestedArguments);
				if (form.ShowDialog(this) == DialogResult.OK)
				{
					row.NestedTemplate = form.SelectedTemplate;
					row.NestedArguments = form.CreateArguments();

					UpdateRow(row);
					UpdatePreview();
				}
			};

			var rowIndex = argumentsPanel.RowCount++;
			argumentsPanel.Controls.Add(new Label { Text = parameter + " =", AutoSize = true, Margin = new Padding(3, 7, 3, 0) }, 0, rowIndex);
			argumentsPanel.Controls.Add(row.KindComboBox, 1, rowIndex);
			argumentsPanel.Controls.Add(row.TargetComboBox, 2, rowIndex);
			argumentsPanel.Controls.Add(row.NestedButton, 3, rowIndex);
			argumentsPanel.Controls.Add(row.PointerCheckBox, 4, rowIndex);

			rows.Add(row);
		}

		private static KindItem FindKind(ArgumentRow row, ArgumentKind kind)
		{
			return row.KindComboBox.Items.Cast<KindItem>().First(i => i.Kind == kind);
		}

		/// <summary>Shows the controls which belong to the selected kind.</summary>
		private void UpdateRow(ArgumentRow row)
		{
			var kind = (row.KindComboBox.SelectedItem as KindItem)?.Kind ?? ArgumentKind.Unset;

			row.TargetComboBox.Visible = kind == ArgumentKind.Class || kind == ArgumentKind.Parameter;
			row.NestedButton.Visible = kind == ArgumentKind.Template;
			row.PointerCheckBox.Enabled = kind != ArgumentKind.Unset && kind != ArgumentKind.Void;
			if (kind == ArgumentKind.Void)
			{
				row.PointerCheckBox.Checked = true;
			}

			switch (kind)
			{
				case ArgumentKind.Class:
				{
					var selectedClass = (row.TargetComboBox.SelectedItem as NamedItem<ClassNode>)?.Value;

					row.TargetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
					row.TargetComboBox.Items.Clear();
					foreach (var c in project.Classes.OrderBy(c => c.Name))
					{
						row.TargetComboBox.Items.Add(new NamedItem<ClassNode> { Value = c, Text = c.Name });
					}

					var item = row.TargetComboBox.Items.Cast<NamedItem<ClassNode>>().FirstOrDefault(i => i.Value == selectedClass);
					if (item != null)
					{
						row.TargetComboBox.SelectedItem = item;
					}
					else if (row.TargetComboBox.Items.Count > 0)
					{
						row.TargetComboBox.SelectedIndex = 0;
					}
					break;
				}
				case ArgumentKind.Parameter:
				{
					// The name of a parameter of the surrounding template. A new name creates a new parameter.
					var text = row.TargetComboBox.DropDownStyle == ComboBoxStyle.DropDown ? row.TargetComboBox.Text : string.Empty;

					row.TargetComboBox.Items.Clear();
					row.TargetComboBox.DropDownStyle = ComboBoxStyle.DropDown;
					foreach (var name in project.Templates.SelectMany(t => t.Parameters).Distinct().OrderBy(n => n))
					{
						row.TargetComboBox.Items.Add(name);
					}
					row.TargetComboBox.Text = string.IsNullOrEmpty(text) ? row.Parameter : text;
					break;
				}
				case ArgumentKind.Template:
					row.NestedButton.Text = row.NestedTemplate == null ? "Choose..." : GetNestedName(row) + " ...";
					break;
			}
		}

		private static string GetNestedName(ArgumentRow row)
		{
			var probe = new TemplateImplementationNode();
			probe.SetTemplate(row.NestedTemplate, row.NestedArguments);
			return probe.TypeName;
		}

		private static BaseNode CreateArgument(ArgumentRow row)
		{
			var item = row.KindComboBox.SelectedItem as KindItem;

			BaseNode node = null;
			switch (item?.Kind ?? ArgumentKind.Unset)
			{
				case ArgumentKind.Unset:
					return null;
				case ArgumentKind.Void:
					return new PointerNode();
				case ArgumentKind.Class:
				{
					var classNode = (row.TargetComboBox.SelectedItem as NamedItem<ClassNode>)?.Value;
					if (classNode == null)
					{
						return null;
					}

					var instanceNode = new ClassInstanceNode();
					instanceNode.ChangeInnerNode(classNode);
					node = instanceNode;
					break;
				}
				case ArgumentKind.Template:
				{
					if (row.NestedTemplate == null)
					{
						return null;
					}

					var implementationNode = new TemplateImplementationNode();
					implementationNode.SetTemplate(row.NestedTemplate, row.NestedArguments);
					node = implementationNode;
					break;
				}
				case ArgumentKind.Parameter:
				{
					var name = row.TargetComboBox.Text.Trim();
					if (!TemplateParameterNode.IsValidParameterName(name))
					{
						return null;
					}

					node = new TemplateParameterNode { ParameterName = name };
					break;
				}
				case ArgumentKind.Primitive:
					node = BaseNode.CreateInstanceFromType(item.NodeType, false);
					break;
				case ArgumentKind.Custom:
					node = row.CustomNode;
					break;
			}

			if (node != null && row.PointerCheckBox.Checked)
			{
				var pointerNode = new PointerNode();
				pointerNode.ChangeInnerNode(node);
				return pointerNode;
			}

			return node;
		}

		private void UpdatePreview()
		{
			var template = SelectedTemplate;
			if (template == null)
			{
				previewLabel.Text = string.Empty;
				return;
			}

			var probe = new TemplateImplementationNode();
			probe.SetTemplate(template, CreateArguments());
			previewLabel.Text = probe.TypeName;
		}
	}
}
