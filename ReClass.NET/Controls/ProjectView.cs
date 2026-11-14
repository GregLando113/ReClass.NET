using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Windows.Forms;
using ReClassNET.Extensions;
using ReClassNET.Nodes;
using ReClassNET.Project;

namespace ReClassNET.Controls
{
	public partial class ProjectView : UserControl
	{
		/// <summary>A custom tree node for class nodes with hierarchical structure.</summary>
		private class ClassTreeNode : TreeNode
		{
			private readonly ProjectView control;

			public ClassNode ClassNode { get; }

			/// <summary>Constructor of the class.</summary>
			/// <param name="node">The class node.</param>
			/// <param name="control">The <see cref="ProjectView"/> instance this node should belong to.</param>
			public ClassTreeNode(ClassNode node, ProjectView control)
				: this(node, control, null)
			{
				Contract.Requires(node != null);
				Contract.Requires(control != null);
			}

			private ClassTreeNode(ClassNode node, ProjectView control, HashSet<ClassNode> seen)
			{
				Contract.Requires(node != null);
				Contract.Requires(control != null);

				ClassNode = node;

				this.control = control;

				Text = node.Name;

				ImageIndex = 1;
				SelectedImageIndex = 1;

				RebuildClassHierarchy(seen ?? new HashSet<ClassNode> { ClassNode });
			}

			public void Update()
			{
				Text = ClassNode.Name;

				RebuildClassHierarchy(new HashSet<ClassNode> { ClassNode });
			}

			/// <summary>Rebuilds the class hierarchy.</summary>
			/// <param name="seen">The already seen classes.</param>
			private void RebuildClassHierarchy(HashSet<ClassNode> seen)
			{
				Contract.Requires(seen != null);

				if (!control.EnableClassHierarchyView)
				{
					return;
				}

				var distinctClasses = ClassNode.Nodes
					.OfType<BaseWrapperNode>()
					.Select(w => w.ResolveMostInnerNode())
					.OfType<ClassNode>()
					.Where(c => !(c is TemplateInstanceClassNode)) // Generated classes are not part of the project.
					.Distinct()
					.ToList();

				if (distinctClasses.IsEquivalentTo(Nodes.Cast<ClassTreeNode>().Select(t => t.ClassNode)))
				{
					return;
				}

				Nodes.Clear();

				foreach (var child in distinctClasses)
				{
					var childSeen = new HashSet<ClassNode>(seen);
					if (childSeen.Add(child))
					{
						Nodes.Add(new ClassTreeNode(child, control, childSeen));
					}
				}

				if (control.AutoExpandClassNodes)
				{
					Expand();
				}
			}
		}

		public class EnumTreeNode : TreeNode
		{
			public EnumDescription Enum { get; }

			public EnumTreeNode(EnumDescription @enum)
			{
				Contract.Requires(@enum != null);

				Enum = @enum;

				ImageIndex = 3;
				SelectedImageIndex = 3;

				Update();
			}

			public void Update()
			{
				Text = Enum.Name;
			}
		}

		/// <summary>A tree node for a class template.</summary>
		private class TemplateTreeNode : TreeNode
		{
			public TemplateNode Template { get; }

			public TemplateTreeNode(TemplateNode template)
			{
				Contract.Requires(template != null);

				Template = template;

				ImageIndex = 4;
				SelectedImageIndex = 4;

				Update();
			}

			public void Update()
			{
				Text = Template.DisplayName;
			}
		}

		private class NodeSorter : IComparer
		{
			public int Compare(object x, object y)
			{
				var compare = Application.CurrentCulture.CompareInfo;

				if (x is ClassTreeNode cn1 && y is ClassTreeNode cn2)
				{
					return compare.Compare(cn1.Text, cn2.Text);
				}
				if (x is EnumTreeNode en1 && y is EnumTreeNode en2)
				{
					return compare.Compare(en1.Text, en2.Text);
				}
				if (x is TemplateTreeNode tt1 && y is TemplateTreeNode tt2)
				{
					return compare.Compare(tt1.Text, tt2.Text);
				}
				if (x is TreeNode tn1 && tn1.Parent == null && y is TreeNode tn2 && tn2.Parent == null)
				{
					return (int)tn1.Tag - (int)tn2.Tag;
				}

				return 0;
			}
		}

		private readonly TreeNode pinnedRootNode;
		private readonly TreeNode enumsRootNode;
		private readonly TreeNode templatesRootNode;
		private readonly TreeNode classesRootNode;

		private ClassNode selectedClass;

		private bool autoExpandClassNodes;
		private bool enableClassHierarchyView;

		public delegate void SelectionChangedEvent(object sender, ClassNode node);
		public event SelectionChangedEvent SelectionChanged;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ClassNode SelectedClass
		{
			get => selectedClass;
			set
			{
				if (selectedClass != value)
				{
					selectedClass = value;
					if (selectedClass is TemplateNode selectedTemplate)
					{
						projectTreeView.SelectedNode = FindTemplateTreeNode(selectedTemplate);
					}
					else if (selectedClass != null && (projectTreeView.SelectedNode as ClassTreeNode)?.ClassNode != selectedClass)
					{
						projectTreeView.SelectedNode = FindMainClassTreeNode(selectedClass);
					}

					SelectionChanged?.Invoke(this, selectedClass);
				}
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public EnumDescription SelectedEnum { get; private set; }

		[DefaultValue(false)]
		public bool AutoExpandClassNodes
		{
			get => autoExpandClassNodes;
			set
			{
				if (autoExpandClassNodes != value)
				{
					autoExpandClassNodes = value;

					if (autoExpandClassNodes)
					{
						ExpandAllClassNodes();
					}
				}
			}
		}

		[DefaultValue(false)]
		public bool EnableClassHierarchyView
		{
			get => enableClassHierarchyView;
			set
			{
				if (enableClassHierarchyView != value)
				{
					enableClassHierarchyView = value;

					var classes = classesRootNode.Nodes.Cast<ClassTreeNode>().Select(t => t.ClassNode).ToList();

					classesRootNode.Nodes.Clear();
					pinnedRootNode.Nodes.Clear();

					AddClasses(classes);
				}
			}
		}

		public ContextMenuStrip ClassesContextMenuStrip { get; set; }

		public ContextMenuStrip ClassContextMenuStrip { get; set; }

		public ContextMenuStrip EnumsContextMenuStrip { get; set; }

		public ContextMenuStrip EnumContextMenuStrip { get; set; }

		public ContextMenuStrip TemplatesContextMenuStrip { get; set; }

		public ContextMenuStrip TemplateContextMenuStrip { get; set; }

		/// <summary>The selected template or null if no template is selected.</summary>
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public TemplateNode SelectedTemplate => selectedClass as TemplateNode;

		public ProjectView()
		{
			Contract.Ensures(classesRootNode != null);
			Contract.Ensures(enumsRootNode != null);

			InitializeComponent();

			DoubleBuffered = true;

			projectTreeView.TreeViewNodeSorter = new NodeSorter();
			projectTreeView.ImageList = new ImageList();
			projectTreeView.ImageList.Images.Add(Properties.Resources.B16x16_Text_List_Bullets);
			projectTreeView.ImageList.Images.Add(Properties.Resources.B16x16_Class_Type);
			projectTreeView.ImageList.Images.Add(Properties.Resources.B16x16_Category);
			projectTreeView.ImageList.Images.Add(Properties.Resources.B16x16_Enum_Type);
			projectTreeView.ImageList.Images.Add(Properties.Resources.B16x16_Custom_Type);

			// Only part of the tree while a class is pinned.
			pinnedRootNode = new TreeNode
			{
				Text = "Pinned",
				ImageIndex = 2,
				SelectedImageIndex = 2,
				Tag = -2
			};

			templatesRootNode = new TreeNode
			{
				Text = "Templates",
				ImageIndex = 2,
				SelectedImageIndex = 2,
				Tag = -1
			};

			projectTreeView.Nodes.Add(templatesRootNode);

			classesRootNode = new TreeNode
			{
				Text = "Classes",
				ImageIndex = 0,
				SelectedImageIndex = 0,
				Tag = 0
			};

			projectTreeView.Nodes.Add(classesRootNode);

			enumsRootNode = new TreeNode
			{
				Text = "Enums",
				ImageIndex = 2,
				SelectedImageIndex = 2,
				Tag = 1
			};

			projectTreeView.Nodes.Add(enumsRootNode);
		}

		#region Event Handler

		private void projectTreeView_AfterSelect(object sender, TreeViewEventArgs e)
		{
			if (e.Node.Level == 0)
			{
				return;
			}

			if (e.Node is ClassTreeNode classTreeNode)
			{
				if (selectedClass != classTreeNode.ClassNode)
				{
					SelectedClass = classTreeNode.ClassNode;
				}
			}
			else if (e.Node is TemplateTreeNode templateTreeNode)
			{
				if (selectedClass != templateTreeNode.Template)
				{
					SelectedClass = templateTreeNode.Template;
				}
			}
			else if (e.Node is EnumTreeNode enumTreeNode)
			{
				SelectedEnum = enumTreeNode.Enum;
			}
		}

		private void projectTreeView_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right)
			{
				return;
			}

			var node = projectTreeView.GetNodeAt(e.X, e.Y);
			if (node == null)
			{
				return;
			}

			ContextMenuStrip cms = null;
			if (node == classesRootNode)
			{
				cms = ClassesContextMenuStrip;
			}
			else if (node is ClassTreeNode)
			{
				cms = ClassContextMenuStrip;

				projectTreeView.SelectedNode = node;
			}
			else if (node == templatesRootNode)
			{
				cms = TemplatesContextMenuStrip;
			}
			else if (node is TemplateTreeNode)
			{
				cms = TemplateContextMenuStrip;

				projectTreeView.SelectedNode = node;
			}
			else if (node == enumsRootNode)
			{
				cms = EnumsContextMenuStrip;
			}
			else if (node is EnumTreeNode)
			{
				cms = EnumContextMenuStrip;

				projectTreeView.SelectedNode = node;
			}
			cms?.Show(projectTreeView, e.Location);
		}

		private void projectTreeView_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
		{
			var isClassTreeNode = e.Node is ClassTreeNode;
			var isEnumTreeNode = e.Node is EnumTreeNode;
			e.CancelEdit = !isClassTreeNode && !isEnumTreeNode && !(e.Node is TemplateTreeNode);
		}

		private void projectTreeView_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
		{
			if (!string.IsNullOrEmpty(e.Label))
			{
				if (e.Node is ClassTreeNode classTreeNode)
				{
					classTreeNode.ClassNode.Name = e.Label;
				}
				else if (e.Node is EnumTreeNode enumTreeNode)
				{
					enumTreeNode.Enum.Name = e.Label;
				}
				else if (e.Node is TemplateTreeNode templateTreeNode)
				{
					// The label shows the parameter list (TArray<T>) but only the name can be changed.
					var name = e.Label.Split('<')[0].Trim();
					if (name.Length > 0)
					{
						templateTreeNode.Template.Name = name;
					}

					e.CancelEdit = true;

					templateTreeNode.Update();
				}
			}
		}

		#endregion

		public void ExpandAllClassNodes()
		{
			classesRootNode.ExpandAll();
		}

		public void CollapseAllClassNodes()
		{
			foreach (var tn in classesRootNode.Nodes.Cast<TreeNode>())
			{
				tn.Collapse();
			}
		}

		/// <summary>
		/// Clears all displayed nodes.
		/// </summary>
		public void Clear()
		{
			Clear(true, true);
		}

		/// <summary>
		/// Clears the selected nodes.
		/// </summary>
		/// <param name="clearClasses">Clears the classes if set.</param>
		/// <param name="clearEnums">Clears the enums if set.</param>
		public void Clear(bool clearClasses, bool clearEnums)
		{
			if (clearClasses)
			{
				classesRootNode.Nodes.Clear();
				templatesRootNode.Nodes.Clear();
				pinnedRootNode.Nodes.Clear();

				UpdatePinnedRootNode();
			}

			if (clearEnums)
			{
				enumsRootNode.Nodes.Clear();
			}
		}

		/// <summary>Adds the class to the view.</summary>
		/// <param name="class">The class to add.</param>
		public void AddClass(ClassNode @class)
		{
			Contract.Requires(@class != null);

			AddClasses(new[] { @class });
		}

		/// <summary>
		/// Adds all classes to the view.
		/// </summary>
		/// <param name="classes">The classes to add.</param>
		public void AddClasses(IEnumerable<ClassNode> classes)
		{
			Contract.Requires(classes != null);

			projectTreeView.BeginUpdate();

			foreach (var @class in classes)
			{
				classesRootNode.Nodes.Add(new ClassTreeNode(@class, this));

				if (@class.IsPinned)
				{
					pinnedRootNode.Nodes.Add(new ClassTreeNode(@class, this));
				}
			}

			UpdatePinnedRootNode();

			classesRootNode.Expand();

			projectTreeView.Sort();

			projectTreeView.EndUpdate();
		}

		/// <summary>Adds the templates to the view.</summary>
		/// <param name="templates">The templates to add.</param>
		public void AddTemplates(IEnumerable<TemplateNode> templates)
		{
			Contract.Requires(templates != null);

			projectTreeView.BeginUpdate();

			foreach (var template in templates)
			{
				templatesRootNode.Nodes.Add(new TemplateTreeNode(template));
			}

			templatesRootNode.Expand();

			projectTreeView.Sort();

			projectTreeView.EndUpdate();
		}

		/// <summary>Adds the template to the view.</summary>
		/// <param name="template">The template to add.</param>
		public void AddTemplate(TemplateNode template)
		{
			Contract.Requires(template != null);

			AddTemplates(new[] { template });
		}

		/// <summary>Removes the template from the view.</summary>
		/// <param name="template">The template to remove.</param>
		public void RemoveTemplate(TemplateNode template)
		{
			Contract.Requires(template != null);

			FindTemplateTreeNode(template)?.Remove();

			if (selectedClass == template)
			{
				SelectedClass = classesRootNode.Nodes.Cast<ClassTreeNode>().FirstOrDefault()?.ClassNode;
			}
		}

		/// <summary>Updates the display for the given template.</summary>
		/// <param name="template">The template to update.</param>
		public void UpdateTemplateNode(TemplateNode template)
		{
			Contract.Requires(template != null);

			var treeNode = FindTemplateTreeNode(template);
			if (treeNode != null && treeNode.Text != template.DisplayName)
			{
				projectTreeView.BeginUpdate();

				treeNode.Update();

				projectTreeView.Sort();

				projectTreeView.EndUpdate();
			}
		}

		private TemplateTreeNode FindTemplateTreeNode(TemplateNode template)
		{
			return templatesRootNode.Nodes
				.Cast<TemplateTreeNode>()
				.FirstOrDefault(t => t.Template == template);
		}

		/// <summary>Removes the class from the view.</summary>
		/// <param name="node">The class to remove.</param>
		public void RemoveClass(ClassNode node)
		{
			Contract.Requires(node != null);

			foreach (var tn in FindClassTreeNodes(node).ToList())
			{
				tn.Remove();
			}

			UpdatePinnedRootNode();

			if (selectedClass == node)
			{
				if (classesRootNode.Nodes.Count > 0)
				{
					projectTreeView.SelectedNode = classesRootNode.Nodes[0];
				}
				else
				{
					SelectedClass = null;
				}
			}
		}

		/// <summary>Searches for the <see cref="ClassTreeNode"/> which represents the class.</summary>
		/// <param name="node">The class to search.</param>
		/// <returns>The found class tree node.</returns>
		private ClassTreeNode FindMainClassTreeNode(ClassNode node)
		{
			Contract.Requires(node != null);

			return classesRootNode.Nodes
				.Cast<ClassTreeNode>()
				.FirstOrDefault(t => t.ClassNode == node);
		}

		/// <summary>Searches for the ClassTreeNode which represents the class.</summary>
		/// <param name="node">The class to search.</param>
		/// <returns>The found class tree node.</returns>
		private IEnumerable<ClassTreeNode> FindClassTreeNodes(ClassNode node)
		{
			Contract.Requires(node != null);

			return classesRootNode.Nodes
				.Cast<ClassTreeNode>()
				.Concat(pinnedRootNode.Nodes.Cast<ClassTreeNode>())
				.Traverse(t => t.Nodes.Cast<ClassTreeNode>())
				.Where(n => n.ClassNode == node);
		}

		/// <summary>
		/// Updates the display for the given class.
		/// </summary>
		/// <param name="class">The class to update.</param>
		public void UpdateClassNode(ClassNode @class)
		{
			Contract.Requires(@class != null);

			projectTreeView.BeginUpdate();

			foreach (var tn in FindClassTreeNodes(@class))
			{
				tn.Update();
			}

			projectTreeView.Sort();

			projectTreeView.EndUpdate();
		}

		/// <summary>
		/// Shows the class in the pinned category if <see cref="ClassNode.IsPinned"/> is set and removes it from there otherwise.
		/// </summary>
		/// <param name="class">The class to update.</param>
		public void UpdatePinnedClass(ClassNode @class)
		{
			Contract.Requires(@class != null);

			var treeNode = pinnedRootNode.Nodes
				.Cast<ClassTreeNode>()
				.FirstOrDefault(t => t.ClassNode == @class);

			if (@class.IsPinned == (treeNode != null))
			{
				return;
			}

			projectTreeView.BeginUpdate();

			if (treeNode != null)
			{
				// Don't let the tree view move the selection to a neighbour of the removed node.
				if (projectTreeView.SelectedNode == treeNode || treeNode.Nodes.Cast<TreeNode>().Traverse(t => t.Nodes.Cast<TreeNode>()).Contains(projectTreeView.SelectedNode))
				{
					projectTreeView.SelectedNode = FindMainClassTreeNode(@class);
				}

				treeNode.Remove();
			}
			else
			{
				pinnedRootNode.Nodes.Add(new ClassTreeNode(@class, this));
			}

			UpdatePinnedRootNode();

			projectTreeView.Sort();

			projectTreeView.EndUpdate();
		}

		/// <summary>Shows the pinned category while it contains classes.</summary>
		private void UpdatePinnedRootNode()
		{
			var isShown = pinnedRootNode.TreeView != null;
			if (pinnedRootNode.Nodes.Count > 0)
			{
				if (!isShown)
				{
					projectTreeView.Nodes.Insert(0, pinnedRootNode);
				}

				pinnedRootNode.Expand();
			}
			else if (isShown)
			{
				pinnedRootNode.Remove();
			}
		}

		/// <summary>Adds the enum to the view.</summary>
		/// <param name="enum">The enum to add.</param>
		public void AddEnum(EnumDescription @enum)
		{
			Contract.Requires(@enum != null);

			AddEnums(new[] { @enum });
		}

		/// <summary>Adds the enums to the view.</summary>
		/// <param name="enums">The enums to add.</param>
		public void AddEnums(IEnumerable<EnumDescription> enums)
		{
			Contract.Requires(enums != null);

			projectTreeView.BeginUpdate();

			foreach (var @enum in enums)
			{
				enumsRootNode.Nodes.Add(new EnumTreeNode(@enum));
			}

			enumsRootNode.ExpandAll();

			projectTreeView.Sort();

			projectTreeView.EndUpdate();
		}

		/// <summary>
		/// Updates the display for the given enum.
		/// </summary>
		/// <param name="enum">The enum to update.</param>
		public void UpdateEnumNode(EnumDescription @enum)
		{
			Contract.Requires(@enum != null);

			projectTreeView.BeginUpdate();

			var node = enumsRootNode.Nodes
				.Cast<EnumTreeNode>()
				.FirstOrDefault(n => n.Enum == @enum);

			if (node != null)
			{
				node.Update();
			}
			else
			{
				AddEnum(@enum);
			}

			projectTreeView.Sort();

			projectTreeView.EndUpdate();
		}
	}
}
