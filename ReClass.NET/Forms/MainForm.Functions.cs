using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ReClassNET.CodeGenerator;
using ReClassNET.Controls;
using ReClassNET.DataExchange.ReClass;
using ReClassNET.Extensions;
using ReClassNET.Logger;
using ReClassNET.Memory;
using ReClassNET.Nodes;
using ReClassNET.Project;
using ReClassNET.UI;

namespace ReClassNET.Forms
{
	public partial class MainForm
	{
		public void ShowPartialCodeGeneratorForm(IReadOnlyList<ClassNode> partialClasses)
		{
			Contract.Requires(partialClasses != null);

			ShowCodeGeneratorForm(partialClasses, new EnumDescription[0], new CppCodeGenerator(currentProject.TypeMapping));
		}

		public void ShowCodeGeneratorForm(ICodeGenerator generator)
		{
			Contract.Requires(generator != null);

			ShowCodeGeneratorForm(currentProject.Classes, currentProject.Enums, generator);
		}

		public void ShowCodeGeneratorForm(IReadOnlyList<ClassNode> classes, IReadOnlyList<EnumDescription> enums, ICodeGenerator generator)
		{
			Contract.Requires(classes != null);
			Contract.Requires(generator != null);
			Contract.Requires(enums != null);

			new CodeForm(generator, classes, enums, Program.Logger).Show();
		}

		public void AttachToProcess(string processName)
		{
			var info = Program.CoreFunctions.EnumerateProcesses().FirstOrDefault(p => string.Equals(p.Name, processName, StringComparison.OrdinalIgnoreCase));
			if (info == null)
			{
				MessageBox.Show($"Process '{processName}' could not be found.", Constants.ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);

				Program.Settings.LastProcess = string.Empty;
			}
			else
			{
				AttachToProcess(info);
			}
		}

		public void AttachToProcess(ProcessInfo info)
		{
			Contract.Requires(info != null);

			Program.RemoteProcess.Close();

			Program.RemoteProcess.Open(info);
			Program.RemoteProcess.UpdateProcessInformations();

			Program.Settings.LastProcess = Program.RemoteProcess.UnderlayingProcess.Name;
		}

		/// <summary>Sets the current project.</summary>
		/// <param name="newProject">The new project.</param>
		public void SetProject(ReClassNetProject newProject)
		{
			Contract.Requires(newProject != null);

			if (currentProject == newProject)
			{
				return;
			}

			if (currentProject != null)
			{
				ClassNode.ClassCreated -= currentProject.AddClass;
			}

			void UpdateClassNodes(BaseNode node)
			{
				projectView.UpdateClassNode((ClassNode)node);
			}

			currentProject = newProject;
			currentProject.ClassAdded += c =>
			{
				projectView.AddClass(c);
				c.NodesChanged += UpdateClassNodes;
				c.NameChanged += UpdateClassNodes;
			};
			currentProject.ClassRemoved += c =>
			{
				projectView.RemoveClass(c);
				c.NodesChanged -= UpdateClassNodes;
				c.NameChanged -= UpdateClassNodes;
			};
			currentProject.EnumAdded += e => { projectView.AddEnum(e); };

			void UpdateTemplateNode(BaseNode node)
			{
				projectView.UpdateTemplateNode((TemplateNode)node);

				memoryViewControl.Invalidate();
			}

			currentProject.TemplateAdded += t =>
			{
				projectView.AddTemplate(t);
				t.NodesChanged += UpdateTemplateNode;
				t.NameChanged += UpdateTemplateNode;
			};
			currentProject.TemplateRemoved += t =>
			{
				projectView.RemoveTemplate(t);
				t.NodesChanged -= UpdateTemplateNode;
				t.NameChanged -= UpdateTemplateNode;
			};

			ClassNode.ClassCreated += currentProject.AddClass;

			projectView.Clear();
			projectView.AddEnums(currentProject.Enums);
			projectView.AddTemplates(currentProject.Templates);
			foreach (var template in currentProject.Templates)
			{
				template.NodesChanged += UpdateTemplateNode;
				template.NameChanged += UpdateTemplateNode;
			}
			projectView.AddClasses(currentProject.Classes);
			CurrentClassNode = currentProject.Classes.FirstOrDefault();
		}

		/// <summary>Opens the <see cref="InputBytesForm"/> and calls <paramref name="callback"/> with the result.</summary>
		/// <param name="title">The title of the input form.</param>
		/// <param name="callback">The function to call afterwards.</param>
		private void AskAddOrInsertBytes(string title, Action<int> callback)
		{
			Contract.Requires(title != null);
			Contract.Requires(callback != null);

			var classNode = CurrentClassNode;
			if (classNode == null)
			{
				return;
			}

			using var ib = new InputBytesForm(classNode.MemorySize)
			{
				Text = title
			};

			if (ib.ShowDialog() == DialogResult.OK)
			{
				callback(ib.Bytes);
			}
		}

		/// <summary>
		/// Adds <paramref name="bytes"/> bytes at the end of the current class.
		/// </summary>
		/// <param name="bytes">Amount of bytes</param>
		public void AddBytesToClass(int bytes)
		{
			Contract.Requires(bytes >= 0);

			var node = memoryViewControl.GetSelectedNodes().Select(h => h.Node).FirstOrDefault();
			if (node == null)
			{
				return;
			}

			var container = node as BaseContainerNode ?? node.GetParentContainer();
			if (container == null || !CanAddBytesToContainer(container, bytes))
			{
				return;
			}

			container.AddBytes(bytes);

			Invalidate();
		}

		/// <summary>
		/// Inserts <paramref name="bytes"/> bytes at the first selected node to the current class.
		/// </summary>
		/// <param name="bytes">Amount of bytes</param>
		public void InsertBytesInClass(int bytes)
		{
			Contract.Requires(bytes >= 0);

			var node = memoryViewControl.GetSelectedNodes().Select(h => h.Node).FirstOrDefault();
			if (node == null)
			{
				return;
			}

			var container = node as BaseContainerNode ?? node.GetParentContainer();
			if (container == null || !CanAddBytesToContainer(container, bytes))
			{
				return;
			}

			container.InsertBytes(node, bytes);

			Invalidate();
		}

		/// <summary>
		/// Checks if <paramref name="bytes"/> bytes can be added to the container without shifting the layout of the classes which embed it.
		/// Shows a message if that's not possible.
		/// </summary>
		private bool CanAddBytesToContainer(BaseContainerNode container, int bytes)
		{
			Contract.Requires(container != null);

			if (IsReadOnlyNode(container))
			{
				return false;
			}

			bool canResize;
			string reason;
			switch (container)
			{
				case ClassNode classNode:
					canResize = StructureLayout.CanResizeClass(classNode, classNode.MemorySize + bytes, CurrentProject.Classes, out reason);
					break;
				case UnionNode unionNode:
					// Every added hex node becomes a separate union member.
					canResize = StructureLayout.CanResize(unionNode, Math.Max(unionNode.MemorySize, Math.Min(bytes, IntPtr.Size)), out reason);
					break;
				default:
					return true;
			}

			if (!canResize)
			{
				ShowStructureLayoutConflict(reason);
			}

			return canResize;
		}

		/// <summary>
		/// Checks if <paramref name="node"/> can change its size to <paramref name="newSize"/> without shifting other nodes.
		/// Shows a message if that's not possible.
		/// </summary>
		private static bool CanResizeNode(BaseNode node, int newSize)
		{
			Contract.Requires(node != null);

			if (!StructureLayout.CanResize(node, newSize, out var reason))
			{
				ShowStructureLayoutConflict(reason);

				return false;
			}

			return true;
		}

		/// <summary>
		/// Tests if the node is generated from a template and can't be changed. Shows a message in this case.
		/// </summary>
		private static bool IsReadOnlyNode(BaseNode node)
		{
			if (node == null || !node.IsInReadOnlyClass())
			{
				return false;
			}

			MessageBox.Show(
				"This part of the structure is generated from a template and can't be changed here. Edit the template instead.",
				"Template",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information
			);

			return true;
		}

		/// <summary>Adds a new template to the project and shows it.</summary>
		private void AddNewTemplate()
		{
			var names = new HashSet<string>(CurrentProject.Templates.Select(t => t.Name));

			var name = "Template";
			for (var i = 2; names.Contains(name); ++i)
			{
				name = $"Template{i}";
			}

			var template = TemplateNode.Create();
			template.Name = name;
			template.AddBytes(4 * IntPtr.Size);

			CurrentProject.AddTemplate(template);

			CurrentClassNode = template;
		}

		/// <summary>Removes the selected template from the project if it's not used.</summary>
		private void DeleteSelectedTemplate()
		{
			var template = projectView.SelectedTemplate;
			if (template == null)
			{
				return;
			}

			try
			{
				CurrentProject.RemoveTemplate(template);
			}
			catch (TemplateReferencedException ex)
			{
				MessageBox.Show(
					ex.Message + Environment.NewLine + string.Join(Environment.NewLine, ex.References.Select(c => c.Name)),
					"Template",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning
				);
			}
		}

		/// <summary>
		/// Lets the user choose the template and the arguments of the implementation and applies them
		/// if this doesn't create a cycle and the surrounding structure has enough room.
		/// </summary>
		private void EditTemplateImplementation(TemplateImplementationNode implementation)
		{
			Contract.Requires(implementation != null);

			if (CurrentProject.Templates.Count == 0)
			{
				MessageBox.Show(
					"There are no templates yet. Add one with a right click on 'Templates' in the project view.",
					"Template",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information
				);

				return;
			}

			var parentClass = implementation.GetParentClass();

			using var form = new TemplateArgumentsForm(CurrentProject, parentClass is TemplateNode, implementation.Template, implementation.Arguments);
			if (form.ShowDialog() != DialogResult.OK)
			{
				return;
			}

			var template = form.SelectedTemplate;
			var arguments = form.CreateArguments();

			// Behind a pointer nothing is embedded, so a template can point to itself (TLink<T>* in TLink<T>).
			if (!implementation.IsBehindPointer && ClassUtil.IsCyclicIfTemplateIsImplemented(parentClass, template, arguments, CurrentProject.Classes))
			{
				MessageBox.Show("Invalid operation because this would create a class cycle.", "Cycle Detected", MessageBoxButtons.OK, MessageBoxIcon.Error);

				return;
			}

			if (implementation.IsBehindPointer)
			{
				CreateNewClassesForArguments(arguments);

				// The size of the surrounding structure doesn't change. The nodes are generated when the pointer is opened.
				implementation.SetTemplate(template, arguments);
				implementation.RebuildLater(CurrentProject);
				implementation.GetParentContainer()?.ChildHasChanged(implementation);

				memoryViewControl.Invalidate();

				return;
			}

			// Build the implementation separately to learn its size.
			var probe = new TemplateImplementationNode();
			probe.SetTemplate(template, arguments);
			probe.Rebuild(CurrentProject, false);

			if (!CanResizeNode(implementation, probe.MemorySize))
			{
				return;
			}

			CreateNewClassesForArguments(arguments);

			implementation.SetTemplate(template, arguments);
			implementation.Rebuild(CurrentProject);

			memoryViewControl.Invalidate();
		}

		/// <summary>
		/// Creates the classes for all arguments which were set to "&lt;new&gt;" in the <see cref="TemplateArgumentsForm"/>
		/// and replaces the placeholders with them. Called once the arguments are accepted, so a cancelled or refused
		/// change leaves no unused classes behind.
		/// </summary>
		private void CreateNewClassesForArguments(IReadOnlyDictionary<string, BaseNode> arguments)
		{
			var created = new Dictionary<ClassNode, ClassNode>();

			var instances = TemplateUtil.Flatten(arguments.Values, false, true)
				.OfType<ClassInstanceNode>()
				.Where(i => !(i is TemplateImplementationNode))
				.ToList();
			foreach (var instance in instances)
			{
				if (!(instance.InnerNode is ClassNode placeholder) || !TemplateArgumentsForm.IsNewClassPlaceholder(placeholder, CurrentProject))
				{
					continue;
				}

				if (!created.TryGetValue(placeholder, out var classNode))
				{
					// The class gets added to the project by its creation.
					classNode = ClassNode.Create();
					classNode.Initialize();

					created.Add(placeholder, classNode);
				}

				instance.ChangeInnerNode(classNode);
			}
		}

		private static void ShowStructureLayoutConflict(string reason)
		{
			MessageBox.Show(
				reason + Environment.NewLine + Environment.NewLine + "Insert bytes after the node first to make room.",
				"Structure Alignment",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning
			);
		}

		/// <summary>
		/// Unselects all selected nodes.
		/// </summary>
		public void ClearSelection()
		{
			memoryViewControl.ClearSelection();
		}

		/// <summary>Shows an <see cref="OpenFileDialog"/> with all valid file extensions.</summary>
		/// <returns>The path to the selected file or null if no file was selected.</returns>
		public static string ShowOpenProjectFileDialog()
		{
			using var ofd = new OpenFileDialog
			{
				CheckFileExists = true,
				Filter = $"All ReClass Types |*{ReClassNetFile.FileExtension};*{ReClassFile.FileExtension};*{ReClassQtFile.FileExtension}"
				         + $"|{ReClassNetFile.FormatName} (*{ReClassNetFile.FileExtension})|*{ReClassNetFile.FileExtension}"
				         + $"|{ReClassFile.FormatName} (*{ReClassFile.FileExtension})|*{ReClassFile.FileExtension}"
				         + $"|{ReClassQtFile.FormatName} (*{ReClassQtFile.FileExtension})|*{ReClassQtFile.FileExtension}"
			};

			if (ofd.ShowDialog() == DialogResult.OK)
			{
				return ofd.FileName;
			}

			return null;
		}

		/// <summary>Loads the file as a new project.</summary>
		/// <param name="path">Full pathname of the file.</param>
		public void LoadProjectFromPath(string path)
		{
			Contract.Requires(path != null);

			var project = new ReClassNetProject();

			LoadProjectFromPath(path, ref project);

			// If the file is a ReClass.NET file remember the path.
			if (Path.GetExtension(path) == ReClassNetFile.FileExtension)
			{
				project.Path = path;
			}

			SetProject(project);
		}

		/// <summary>Loads the file into the given project.</summary>
		/// <param name="path">Full pathname of the file.</param>
		/// <param name="project">[in,out] The project.</param>
		private static void LoadProjectFromPath(string path, ref ReClassNetProject project)
		{
			Contract.Requires(path != null);
			Contract.Requires(project != null);
			Contract.Ensures(Contract.ValueAtReturn(out project) != null);

			IReClassImport import;
			switch (Path.GetExtension(path)?.ToLower())
			{
				case ReClassNetFile.FileExtension:
					import = new ReClassNetFile(project);
					break;
				case ReClassQtFile.FileExtension:
					import = new ReClassQtFile(project);
					break;
				case ReClassFile.FileExtension:
					import = new ReClassFile(project);
					break;
				default:
					Program.Logger.Log(LogLevel.Error, $"The file '{path}' has an unknown type.");
					return;
			}
			import.Load(path, Program.Logger);
		}

		/// <summary>Loads all symbols for the current process and displays the progress status.</summary>
		private void LoadAllSymbolsForCurrentProcess()
		{
			if (loadSymbolsTask != null && !loadSymbolsTask.IsCompleted)
			{
				return;
			}

			infoToolStripStatusLabel.Visible = true;

			var index = 0;

			var progress = new Progress<Tuple<Module, IReadOnlyList<Module>>>(
				report =>
				{
					infoToolStripStatusLabel.Text = $"[{++index}/{report.Item2.Count}] Loading symbols for module: {report.Item1.Name}";
				}
			);

			loadSymbolsTaskToken = new CancellationTokenSource();

			loadSymbolsTask = Program.RemoteProcess
				.LoadAllSymbolsAsync(progress, loadSymbolsTaskToken.Token)
				.ContinueWith(_ => infoToolStripStatusLabel.Visible = false, TaskScheduler.FromCurrentSynchronizationContext());
		}

		public void ReplaceSelectedNodesWithType(Type type)
		{
			Contract.Requires(type != null);
			Contract.Requires(type.IsSubclassOf(typeof(BaseNode)));

			var selectedNodes = memoryViewControl.GetSelectedNodes();

			if (IsReadOnlyNode(selectedNodes.Select(s => s.Node).FirstOrDefault(n => n.IsInReadOnlyClass())))
			{
				return;
			}

			var newSelected = new List<MemoryViewControl.SelectedNodeInfo>(selectedNodes.Count);

			var hotSpotPartitions = selectedNodes
				.WhereNot(s => s.Node is ClassNode)
				.GroupBy(s => s.Node.GetParentContainer())
				.Select(g => new
				{
					Container = g.Key,
					Partitions = g.OrderBy(s => s.Node.Offset)
						.GroupWhile((s1, s2) => s1.Node.Offset + s1.Node.MemorySize == s2.Node.Offset)
				});

			var conflicts = new List<string>();

			foreach (var containerPartitions in hotSpotPartitions)
			{
				containerPartitions.Container.BeginUpdate();

				foreach (var partition in containerPartitions.Partitions)
				{
					var hotSpotsToReplace = new Queue<MemoryViewControl.SelectedNodeInfo>(partition);
					while (hotSpotsToReplace.Count > 0)
					{
						var selected = hotSpotsToReplace.Dequeue();

						// The node may have been absorbed by a previous (larger) replacement.
						if (!containerPartitions.Container.ContainsNode(selected.Node))
						{
							continue;
						}

						var node = BaseNode.CreateInstanceFromType(type);

						if (!containerPartitions.Container.CanHandleChildNode(node))
						{
							node.GetUserInterfaceInfo(out var typeName, out _);

							conflicts.Add(node is TemplateParameterNode
								? "A template parameter can only be used in a template."
								: $"'{typeName}' can't be used here.");

							RemoveClassCreatedForNode(node);

							continue;
						}

						// Some nodes adapt their size to the replaced node.
						node.CopyFromNode(selected.Node);

						if (!StructureLayout.CanResize(selected.Node, node.MemorySize, out var reason))
						{
							conflicts.Add(reason);

							RemoveClassCreatedForNode(node);

							continue;
						}

						var createdNodes = new List<BaseNode>();
						containerPartitions.Container.ReplaceChildNode(selected.Node, node, ref createdNodes);

						node.IsSelected = true;

						var info = new MemoryViewControl.SelectedNodeInfo(node, selected.Process, selected.Memory, selected.Address, selected.Level);

						newSelected.Add(info);

						// If more than one node is selected I assume the user wants to replace the complete range with the desired node type.
						if (selectedNodes.Count > 1)
						{
							foreach (var createdNode in createdNodes)
							{
								hotSpotsToReplace.Enqueue(new MemoryViewControl.SelectedNodeInfo(createdNode, selected.Process, selected.Memory, selected.Address + createdNode.Offset - node.Offset, selected.Level));
							}
						}
					}
				}

				containerPartitions.Container.EndUpdate();
			}

			memoryViewControl.ClearSelection();

			if (newSelected.Count > 0)
			{
				memoryViewControl.SetSelectedNodes(newSelected);
			}

			if (conflicts.Count > 0)
			{
				ShowStructureLayoutConflict($"{conflicts.Count} node(s) were not replaced:" + Environment.NewLine + string.Join(Environment.NewLine, conflicts.Distinct()));
			}

			// A new template implementation is empty until a template is chosen.
			if (newSelected.Count == 1 && newSelected[0].Node is TemplateImplementationNode newImplementation)
			{
				EditTemplateImplementation(newImplementation);
			}
		}

		/// <summary>
		/// Removes the class which got created by the initialization of <paramref name="node"/> (e.g. a new class instance or pointer) if the node is discarded.
		/// Must only be called for freshly created nodes.
		/// </summary>
		private void RemoveClassCreatedForNode(BaseNode node)
		{
			if (node is BaseWrapperNode wrapperNode && wrapperNode.ResolveMostInnerNode() is ClassNode classNode && CurrentProject.ContainsClass(classNode.Uuid))
			{
				try
				{
					CurrentProject.Remove(classNode);
				}
				catch (ClassReferencedException)
				{
					// Should not happen for a fresh class.
				}
			}
		}

		private void FindWhatInteractsWithSelectedNode(bool writeOnly)
		{
			var selectedNode = memoryViewControl.GetSelectedNodes().FirstOrDefault();
			if (selectedNode == null)
			{
				return;
			}

			LinkedWindowFeatures.FindWhatInteractsWithAddress(selectedNode.Address, selectedNode.Node.MemorySize, writeOnly);
		}

		private void CopySelectedNodesToClipboard()
		{
			var selectedNodes = memoryViewControl.GetSelectedNodes();
			if (selectedNodes.Count > 0)
			{
				ReClassClipboard.Copy(selectedNodes.Select(h => h.Node), Program.Logger);
			}
		}

		/// <summary>
		/// Copies a Markdown readout of the selected nodes with their current values to the clipboard.
		/// </summary>
		private void CopySelectedNodesReadoutToClipboard()
		{
			var selectedNodes = memoryViewControl.GetSelectedNodes();
			if (selectedNodes.Count == 0)
			{
				return;
			}

			try
			{
				var text = NodeReadout.CreateMarkdown(
					selectedNodes.Select(s => new NodeReadout.Item(s.Node, s.Memory, s.Address)),
					Program.RemoteProcess
				);

				Clipboard.SetText(text);
			}
			catch (Exception ex)
			{
				Program.Logger.Log(ex);
			}
		}

		private void PasteNodeFromClipboardToSelection()
		{
			var (classNodes, nodes) = ReClassClipboard.Paste(CurrentProject, Program.Logger);
			foreach (var pastedClassNode in classNodes)
			{
				if (!CurrentProject.ContainsClass(pastedClassNode.Uuid))
				{
					CurrentProject.AddClass(pastedClassNode);
				}
			}

			var selectedNodes = memoryViewControl.GetSelectedNodes();
			if (selectedNodes.Count != 1)
			{
				return;
			}

			var selected = selectedNodes[0];
			var selectedNode = selected.Node;
			if (selectedNode is ClassNode || IsReadOnlyNode(selectedNode))
			{
				return;
			}

			var containerNode = selectedNode.GetParentContainer();
			var classNode = selectedNode.GetParentClass();
			if (containerNode == null || classNode == null || !containerNode.ContainsNode(selectedNode))
			{
				return;
			}

			var nodesToPaste = new List<BaseNode>();
			foreach (var node in nodes)
			{
				if (node is BaseWrapperNode)
				{
					var rootWrapper = node.GetRootWrapperNode();
					Debug.Assert(rootWrapper == node);

					if (rootWrapper.ShouldPerformCycleCheckForInnerNode())
					{
						if (rootWrapper.ResolveMostInnerNode() is ClassNode innerNode)
						{
							if (!IsCycleFree(classNode, innerNode))
							{
								continue;
							}
						}
					}
				}

				if (containerNode.CanHandleChildNode(node))
				{
					nodesToPaste.Add(node);
				}
			}

			if (nodesToPaste.Count == 0)
			{
				return;
			}

			// The pasted nodes were loaded with a temporary project. Generate the template implementations with the real one.
			foreach (var implementation in TemplateUtil.Flatten(nodesToPaste, false, false).OfType<TemplateImplementationNode>().ToList())
			{
				if (implementation.IsBehindPointer)
				{
					implementation.RebuildLater(CurrentProject);
				}
				else
				{
					implementation.Rebuild(CurrentProject, false);
				}
			}

			// Pasting follows the replacement rules: the pasted nodes replace the selected node and
			// absorb the following undefined bytes if they are larger.
			var pastedSize = containerNode is UnionNode
				? nodesToPaste.Max(n => n.MemorySize)
				: nodesToPaste.Sum(n => n.MemorySize);
			if (!CanResizeNode(selectedNode, pastedSize))
			{
				return;
			}

			var baseOffset = selectedNode.Offset;

			containerNode.ReplaceChildNodeWithNodes(selectedNode, nodesToPaste);

			memoryViewControl.ClearSelection();

			var newSelected = nodesToPaste
				.Select(n =>
				{
					n.IsSelected = true;

					return new MemoryViewControl.SelectedNodeInfo(n, selected.Process, selected.Memory, selected.Address + n.Offset - baseOffset, selected.Level);
				})
				.ToList();
			memoryViewControl.SetSelectedNodes(newSelected);
		}

		private void EditSelectedNodeName()
		{
			var selectedNodes = memoryViewControl.GetSelectedNodes();
			if (selectedNodes.Count == 1)
			{
				memoryViewControl.ShowNodeNameEditBox(selectedNodes[0].Node);
			}
		}

		private void RemoveSelectedNodes()
		{
			// Typed nodes in classes get converted back to hex nodes to keep the layout. Hex nodes get removed.
			if (IsReadOnlyNode(memoryViewControl.GetSelectedNodes().Select(h => h.Node).FirstOrDefault(n => !(n is ClassNode) && n.IsInReadOnlyClass())))
			{
				return;
			}

			var nodesByContainer = memoryViewControl.GetSelectedNodes()
				.Select(h => h.Node)
				.WhereNot(n => n is ClassNode)
				.GroupBy(n => n.GetParentContainer())
				.Where(g => g.Key != null);

			foreach (var group in nodesByContainer)
			{
				group.Key.BeginUpdate();

				foreach (var node in group)
				{
					group.Key.UndefineOrRemoveNode(node);
				}

				group.Key.EndUpdate();
			}

			ClearSelection();
		}

		private void HideSelectedNodes()
		{
			foreach (var hotSpot in memoryViewControl.GetSelectedNodes())
			{
				hotSpot.Node.IsHidden = true;
			}

			ClearSelection();
		}

		private void UnhideChildNodes()
		{
			var selectedNodes = memoryViewControl.GetSelectedNodes();
			if (selectedNodes.Count != 1)
			{
				return;
			}

			if (!(selectedNodes[0].Node is BaseContainerNode containerNode))
			{
				return;
			}

			foreach (var bn in containerNode.Nodes)
			{
				bn.IsHidden = false;
				bn.IsSelected = false;
			}

			containerNode.IsSelected = false;

			ClearSelection();
		}

		private void UnhideNodesBelow()
		{
			var selectedNodes = memoryViewControl.GetSelectedNodes();
			if (selectedNodes.Count != 1)
			{
				return;
			}

			var selectedNode = selectedNodes[0].Node;

			var parentNode = selectedNode.GetParentContainer();
			if (parentNode == null)
			{
				return;
			}

			var hiddenNodeStartIndex = parentNode.FindNodeIndex(selectedNode) + 1;
			if (hiddenNodeStartIndex >= parentNode.Nodes.Count)
			{
				return;
			}

			for (var i = hiddenNodeStartIndex; i < parentNode.Nodes.Count; i++)
			{
				var indexNode = parentNode.Nodes[i];
				if (indexNode.IsHidden)
				{
					indexNode.IsHidden = false;
					indexNode.IsSelected = false;
				}
				else
				{
					break;
				}
			}

			selectedNode.IsSelected = false;

			ClearSelection();
		}

		private void UnhideNodesAbove()
		{
			var selectedNodes = memoryViewControl.GetSelectedNodes();
			if (selectedNodes.Count != 1)
			{
				return;
			}

			var selectedNode = selectedNodes[0].Node;

			var parentNode = selectedNode.GetParentContainer();
			if (parentNode == null)
			{
				return;
			}

			var hiddenNodeStartIndex = parentNode.FindNodeIndex(selectedNode) - 1;
			if (hiddenNodeStartIndex < 0)
			{
				return;
			}

			for (var i = hiddenNodeStartIndex; i > -1; i--)
			{
				var indexNode = parentNode.Nodes[i];
				if (indexNode.IsHidden)
				{
					indexNode.IsHidden = false;
					indexNode.IsSelected = false;
				}
				else
				{
					break;
				}
			}

			selectedNode.IsSelected = false;

			ClearSelection();
		}

		private bool IsCycleFree(ClassNode parent, ClassNode node)
		{
			if (ClassUtil.IsCyclicIfClassIsAccessibleFromParent(parent, node, CurrentProject.Classes))
			{
				MessageBox.Show("Invalid operation because this would create a class cycle.", "Cycle Detected", MessageBoxButtons.OK, MessageBoxIcon.Error);

				return false;
			}

			return true;
		}
	}
}
