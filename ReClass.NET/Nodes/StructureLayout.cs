using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace ReClassNET.Nodes
{
	/// <summary>
	/// Checks if a node can change its size without shifting the offsets of other nodes.
	/// A node may only grow if enough undefined bytes (<see cref="BaseHexNode"/>) follow it in its class.
	/// </summary>
	public static class StructureLayout
	{
		/// <summary>Gets the number of undefined bytes (consecutive hex nodes) which directly follow the node at <paramref name="index"/>.</summary>
		/// <param name="container">The container.</param>
		/// <param name="index">The index of the node.</param>
		/// <returns>The number of bytes.</returns>
		public static int GetHexSlackAfter(BaseContainerNode container, int index)
		{
			Contract.Requires(container != null);

			var slack = 0;
			for (var i = index + 1; i < container.Nodes.Count && container.Nodes[i] is BaseHexNode; ++i)
			{
				slack += container.Nodes[i].MemorySize;
			}
			return slack;
		}

		/// <summary>
		/// Checks if <paramref name="node"/> can change its size to <paramref name="newSize"/> while keeping the layout of the containing class.
		/// Wrapped nodes and union members are checked through their parents.
		/// </summary>
		/// <param name="node">The node which changes its size. Must not be a <see cref="ClassNode"/> (use <see cref="CanResizeClass"/>).</param>
		/// <param name="newSize">The new size of the node.</param>
		/// <param name="reason">[out] The reason if the resize is not possible.</param>
		/// <returns>True if the node can be resized, false otherwise.</returns>
		public static bool CanResize(BaseNode node, int newSize, out string reason)
		{
			Contract.Requires(node != null);

			if (node is ClassNode)
			{
				throw new ArgumentException($"Use {nameof(CanResizeClass)} for class nodes.", nameof(node));
			}

			reason = null;

			if (newSize <= node.MemorySize)
			{
				return true;
			}

			switch (node.ParentNode)
			{
				case BaseWrapperNode wrapperNode when wrapperNode.InnerNode == node:
					return CanResize(wrapperNode, wrapperNode.CalculateSizeForInner(newSize), out reason);

				case UnionNode unionNode:
				{
					var otherMaxSize = unionNode.Nodes
						.Where(n => n != node)
						.Select(n => n.MemorySize)
						.DefaultIfEmpty(0)
						.Max();

					return CanResize(unionNode, Math.Max(newSize, otherMaxSize), out reason);
				}

				case TemplateInstanceClassNode instanceClass:
				{
					// A generated class has no undefined bytes of its own. The growth is passed to the implementation node.
					var owner = instanceClass.Owner;
					if (owner == null)
					{
						return true;
					}

					return CanResize(owner, owner.MemorySize + newSize - node.MemorySize, out reason);
				}

				case ClassNode classNode:
				{
					var index = classNode.FindNodeIndex(node);
					if (index == -1)
					{
						return true;
					}

					var delta = newSize - node.MemorySize;
					var slack = GetHexSlackAfter(classNode, index);
					if (slack >= delta)
					{
						return true;
					}

					reason = $"'{node.Name}' at offset 0x{node.Offset:X} in class '{classNode.Name}' needs {delta} more byte(s), but only {slack} undefined byte(s) follow it.";

					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Checks if the class can change its size to <paramref name="newSize"/> while keeping the layout of every class which embeds it.
		/// </summary>
		/// <param name="classNode">The class which changes its size.</param>
		/// <param name="newSize">The new size of the class.</param>
		/// <param name="classes">All classes of the project.</param>
		/// <param name="reason">[out] The reason if the resize is not possible.</param>
		/// <returns>True if the class can be resized, false otherwise.</returns>
		public static bool CanResizeClass(ClassNode classNode, int newSize, IEnumerable<ClassNode> classes, out string reason)
		{
			Contract.Requires(classNode != null);
			Contract.Requires(classes != null);

			reason = null;

			if (newSize <= classNode.MemorySize)
			{
				return true;
			}

			var reasons = new List<string>();
			if (classNode is TemplateNode templateNode)
			{
				// Every implementation of the template grows by the same amount.
				var delta = newSize - classNode.MemorySize;
				foreach (var implementation in TemplateUtil.EnumerateImplementations(classes, true).Where(i => i.Template == templateNode))
				{
					if (!CanResize(implementation, implementation.MemorySize + delta, out var implementationReason))
					{
						reasons.Add(implementationReason);
					}
				}
			}
			else
			{
				foreach (var wrapperNode in FindEmbeddings(classNode, classes))
				{
					if (!CanResize(wrapperNode, wrapperNode.CalculateSizeForInner(newSize), out var wrapperReason))
					{
						reasons.Add(wrapperReason);
					}
				}
			}

			if (reasons.Count == 0)
			{
				return true;
			}

			reason = $"The class '{classNode.Name}' can't grow by {newSize - classNode.MemorySize} byte(s) because it is embedded without enough undefined bytes after it:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, reasons);

			return false;
		}

		/// <summary>
		/// Finds all wrapper nodes which embed the memory of <paramref name="classNode"/> (class instances, possibly inside arrays or unions).
		/// Pointers are ignored because they don't embed the memory of the class.
		/// </summary>
		/// <param name="classNode">The class to search.</param>
		/// <param name="classes">All classes of the project.</param>
		/// <returns>The wrapper nodes whose inner node is <paramref name="classNode"/>.</returns>
		public static IEnumerable<BaseWrapperNode> FindEmbeddings(ClassNode classNode, IEnumerable<ClassNode> classes)
		{
			Contract.Requires(classNode != null);
			Contract.Requires(classes != null);

			IEnumerable<BaseWrapperNode> Visit(BaseNode node)
			{
				switch (node)
				{
					case PointerNode _:
					case ArrayBufferNode _: // Pointers don't embed the memory of their target.
						break;
					case TemplateInstanceClassNode instanceClass: // Generated classes are not part of the class list.
						foreach (var wrapperNode in instanceClass.Nodes.SelectMany(Visit))
						{
							yield return wrapperNode;
						}
						break;
					case ClassNode _: // Other classes get visited separately.
						break;
					case BaseContainerNode containerNode:
						foreach (var wrapperNode in containerNode.Nodes.SelectMany(Visit))
						{
							yield return wrapperNode;
						}
						break;
					case BaseWrapperNode wrapperNode:
						if (wrapperNode.InnerNode == classNode)
						{
							yield return wrapperNode;
						}
						else if (wrapperNode.InnerNode != null)
						{
							foreach (var innerWrapperNode in Visit(wrapperNode.InnerNode))
							{
								yield return innerWrapperNode;
							}
						}
						break;
				}
			}

			return classes.SelectMany(c => c.Nodes.SelectMany(Visit));
		}
	}
}
