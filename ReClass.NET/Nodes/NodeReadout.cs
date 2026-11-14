using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ReClassNET.Extensions;
using ReClassNET.Memory;

namespace ReClassNET.Nodes
{
	/// <summary>
	/// Creates a Markdown readout of nodes with their current values.
	/// Fields are listed as a table of name and value. Arrays and array buffers of classes become a table with one row
	/// per element and one column per field. Pointers are printed as address and not followed.
	/// </summary>
	public class NodeReadout
	{
		/// <summary>A node with the memory of its container and the address of the node.</summary>
		public class Item
		{
			public BaseNode Node { get; }
			public MemoryBuffer Memory { get; }
			public IntPtr Address { get; }

			public Item(BaseNode node, MemoryBuffer memory, IntPtr address)
			{
				Node = node;
				Memory = memory;
				Address = address;
			}
		}

		/// <summary>Arrays of simple values with at most this many elements are printed inside a table cell.</summary>
		private const int MaxInlineArrayCount = 16;

		/// <summary>Buffers larger than this are not read. Protects against garbage counts.</summary>
		private const long MaxBufferBytes = 256 * 1024 * 1024;

		private const int MaxStringCharacterCount = 256;

		private readonly IRemoteMemoryReader reader;
		private readonly StringBuilder sb = new StringBuilder();

		private NodeReadout(IRemoteMemoryReader reader)
		{
			this.reader = reader;
		}

		/// <summary>Creates the Markdown readout for the given nodes.</summary>
		/// <param name="items">The nodes to print.</param>
		/// <param name="reader">The reader used for array buffers and text pointers. Can be null.</param>
		public static string CreateMarkdown(IEnumerable<Item> items, IRemoteMemoryReader reader)
		{
			var readout = new NodeReadout(reader);

			var list = items.Where(i => i?.Node != null && i.Memory != null).ToList();

			// A selected class is printed completely.
			foreach (var item in list.Where(i => i.Node is ClassNode))
			{
				var classNode = (ClassNode)item.Node;

				readout.WriteHeading(2, $"{classNode.Name} @ {FormatAddress(item.Address)}");
				readout.WriteFields(classNode.Nodes, item.Memory, item.Address, 2, string.Empty);
			}

			// Other nodes are grouped by the structure they belong to.
			var groups = list
				.Where(i => !(i.Node is ClassNode))
				.GroupBy(i => new { Container = i.Node.GetParentContainer(), Base = i.Address.ToInt64() - i.Node.Offset });
			foreach (var group in groups)
			{
				var first = group.First();
				var baseAddress = AddOffset(first.Address, -first.Node.Offset);

				var title = group.Key.Container is ClassNode parentClass ? parentClass.Name : group.Key.Container?.Name;

				readout.WriteHeading(2, $"{title} @ {FormatAddress(baseAddress)}");
				readout.WriteFields(group.OrderBy(i => i.Node.Offset).Select(i => i.Node), first.Memory, baseAddress, 2, string.Empty);
			}

			return readout.sb.ToString().TrimEnd() + Environment.NewLine;
		}

		private static string FormatAddress(IntPtr address) => "0x" + address.ToString(Constants.AddressHexFormat);

		private static IntPtr AddOffset(IntPtr address, long offset)
		{
			var value = address.ToInt64() + offset;

			return IntPtr.Size == 4 ? new IntPtr(unchecked((int)value)) : new IntPtr(value);
		}

		private static MemoryBuffer Shift(MemoryBuffer memory, int offset)
		{
			if (offset == 0)
			{
				return memory;
			}

			var shifted = memory.Clone();
			shifted.Offset += offset;
			return shifted;
		}

		private static string Escape(string text)
		{
			// Angle brackets (TArray<T>) would be read as HTML by some Markdown renderers.
			return (text ?? string.Empty)
				.Replace("\\", "\\\\")
				.Replace("|", "\\|")
				.Replace("<", "\\<")
				.Replace("\r", " ")
				.Replace("\n", " ");
		}

		private void WriteHeading(int level, string text)
		{
			if (sb.Length > 0)
			{
				sb.AppendLine();
			}

			sb.Append('#', Math.Min(level, 6)).Append(' ').AppendLine(text.Replace("<", "\\<"));
			sb.AppendLine();
		}

		private void WriteRow(IEnumerable<string> cells)
		{
			sb.Append("| ").Append(string.Join(" | ", cells.Select(Escape))).AppendLine(" |");
		}

		private void WriteTableHeader(IReadOnlyList<string> columns)
		{
			WriteRow(columns);
			WriteRow(columns.Select(_ => "---"));
		}

		/// <summary>Writes a table of the nodes followed by the details of nested classes, unions and arrays.</summary>
		/// <param name="nodes">The nodes of one container.</param>
		/// <param name="memory">The memory of the container.</param>
		/// <param name="baseAddress">The address of the container.</param>
		/// <param name="level">The heading level of the container.</param>
		/// <param name="path">The path of the container used for the headings of nested structures.</param>
		private void WriteFields(IEnumerable<BaseNode> nodes, MemoryBuffer memory, IntPtr baseAddress, int level, string path)
		{
			var list = nodes.Where(n => !n.IsHidden && !(n is FunctionNode)).ToList();

			WriteTableHeader(new[] { "Offset", "Type", "Name", "Value" });
			foreach (var node in list)
			{
				WriteRow(new[] { $"0x{node.Offset:X04}", TemplateUtil.GetTypeName(node), node.Name, GetValueText(node, memory, baseAddress) });
			}

			foreach (var node in list)
			{
				var nodePath = string.IsNullOrEmpty(path) ? node.Name : $"{path}.{node.Name}";
				var address = AddOffset(baseAddress, node.Offset);

				switch (node)
				{
					case ClassInstanceNode instanceNode when instanceNode.InnerNode is ClassNode innerClass:
						WriteHeading(level + 1, $"{nodePath} : {innerClass.Name} @ {FormatAddress(address)}");
						WriteFields(innerClass.Nodes, Shift(memory, node.Offset), address, level + 1, nodePath);
						break;
					case UnionNode unionNode:
						WriteHeading(level + 1, $"{nodePath} : union @ {FormatAddress(address)}");
						WriteFields(unionNode.Nodes, Shift(memory, node.Offset), address, level + 1, nodePath);
						break;
					case ArrayBufferNode bufferNode:
					{
						bufferNode.UpdateCountFromField(memory);

						var pointer = memory.ReadIntPtr(node.Offset);
						var elementMemory = ReadBuffer(pointer, bufferNode);
						if (elementMemory != null)
						{
							WriteHeading(level + 1, $"{nodePath} : {TemplateUtil.GetTypeName(bufferNode.InnerNode)}[{bufferNode.Count}] @ {FormatAddress(pointer)}");
							WriteArray(bufferNode.InnerNode, bufferNode.Count, elementMemory, pointer, level + 1, nodePath);
						}
						break;
					}
					case BaseWrapperArrayNode arrayNode when arrayNode.InnerNode != null && !IsInlineArray(arrayNode):
						WriteHeading(level + 1, $"{nodePath} : {TemplateUtil.GetTypeName(arrayNode.InnerNode)}[{arrayNode.Count}] @ {FormatAddress(address)}");
						WriteArray(arrayNode.InnerNode, arrayNode.Count, Shift(memory, node.Offset), address, level + 1, nodePath);
						break;
				}
			}
		}

		/// <summary>Reads the elements of an array buffer. Returns null if the buffer is empty or can't be read.</summary>
		private MemoryBuffer ReadBuffer(IntPtr pointer, ArrayBufferNode bufferNode)
		{
			if (reader == null || pointer.IsNull() || bufferNode.InnerNode == null || bufferNode.Count <= 0)
			{
				return null;
			}

			var size = (long)bufferNode.InnerNode.MemorySize * bufferNode.Count;
			if (size <= 0 || size > MaxBufferBytes)
			{
				return null;
			}

			var elementMemory = new MemoryBuffer { Size = (int)size };
			elementMemory.UpdateFrom(reader, pointer);

			return elementMemory.ContainsValidData ? elementMemory : null;
		}

		/// <summary>True if the array is short enough and simple enough to be printed in a table cell.</summary>
		private static bool IsInlineArray(BaseWrapperArrayNode arrayNode)
		{
			return arrayNode.Count <= MaxInlineArrayCount && IsSimpleValue(arrayNode.InnerNode);
		}

		private static bool IsSimpleValue(BaseNode node)
		{
			switch (node)
			{
				case null:
				case ClassInstanceNode _:
				case BaseContainerNode _:
				case ArrayBufferNode _:
				case BaseWrapperArrayNode _:
					return false;
			}

			return true;
		}

		/// <summary>
		/// Writes the elements of an array. Elements which are classes get one row per element and one column per field.
		/// </summary>
		/// <param name="elementNode">The node which describes one element.</param>
		/// <param name="count">The number of elements.</param>
		/// <param name="memory">The memory positioned at the first element.</param>
		/// <param name="address">The address of the first element.</param>
		/// <param name="level">The heading level of the array.</param>
		/// <param name="path">The path of the array.</param>
		private void WriteArray(BaseNode elementNode, int count, MemoryBuffer memory, IntPtr address, int level, string path)
		{
			var elementSize = elementNode.MemorySize;

			if (elementNode is ClassInstanceNode instanceNode && instanceNode.InnerNode is ClassNode elementClass)
			{
				var columns = new List<KeyValuePair<string, Func<MemoryBuffer, IntPtr, string>>>();
				CollectColumns(elementClass.Nodes, string.Empty, 0, columns);

				WriteTableHeader(new[] { "#" }.Concat(columns.Select(c => c.Key)).ToList());
				for (var i = 0; i < count; ++i)
				{
					var elementMemory = Shift(memory, i * elementSize);
					var elementAddress = AddOffset(address, (long)i * elementSize);

					WriteRow(new[] { i.ToString() }.Concat(columns.Select(c => c.Value(elementMemory, elementAddress))));
				}

				return;
			}

			if (IsSimpleValue(elementNode) || elementNode is ArrayBufferNode)
			{
				WriteTableHeader(new[] { "#", "Value" });
				for (var i = 0; i < count; ++i)
				{
					WriteRow(new[] { i.ToString(), GetValueText(elementNode, Shift(memory, i * elementSize), AddOffset(address, (long)i * elementSize)) });
				}

				return;
			}

			// Arrays of arrays or unions: every element gets its own section.
			for (var i = 0; i < count; ++i)
			{
				var elementMemory = Shift(memory, i * elementSize);
				var elementAddress = AddOffset(address, (long)i * elementSize);
				var elementPath = $"{path}[{i}]";

				WriteHeading(level + 1, $"{elementPath} @ {FormatAddress(elementAddress)}");

				switch (elementNode)
				{
					case UnionNode unionNode:
						WriteFields(unionNode.Nodes, elementMemory, elementAddress, level + 1, elementPath);
						break;
					case BaseWrapperArrayNode arrayNode when arrayNode.InnerNode != null:
						WriteArray(arrayNode.InnerNode, arrayNode.Count, elementMemory, elementAddress, level + 1, elementPath);
						break;
				}
			}
		}

		/// <summary>Collects the columns of a class. Nested classes and unions are flattened into dotted column names.</summary>
		private void CollectColumns(IEnumerable<BaseNode> nodes, string prefix, int baseOffset, List<KeyValuePair<string, Func<MemoryBuffer, IntPtr, string>>> columns)
		{
			foreach (var node in nodes.Where(n => !n.IsHidden && !(n is FunctionNode)))
			{
				var name = prefix + node.Name;

				switch (node)
				{
					case ClassInstanceNode instanceNode when instanceNode.InnerNode is ClassNode innerClass:
						CollectColumns(innerClass.Nodes, name + ".", baseOffset + node.Offset, columns);
						break;
					case UnionNode unionNode:
						CollectColumns(unionNode.Nodes, name + ".", baseOffset + node.Offset, columns);
						break;
					default:
					{
						var offset = baseOffset;
						var current = node;
						columns.Add(new KeyValuePair<string, Func<MemoryBuffer, IntPtr, string>>(
							name,
							(memory, address) => GetValueText(current, Shift(memory, offset), AddOffset(address, offset))
						));
						break;
					}
				}
			}
		}

		/// <summary>Gets the value of a node as text.</summary>
		/// <param name="node">The node.</param>
		/// <param name="memory">The memory of the container of the node.</param>
		/// <param name="baseAddress">The address of the container of the node.</param>
		private string GetValueText(BaseNode node, MemoryBuffer memory, IntPtr baseAddress)
		{
			var offset = node.Offset;

			switch (node)
			{
				case BaseHexNode _:
					return string.Join(" ", memory.ReadBytes(offset, node.MemorySize).Select(b => b.ToString("X02")));

				case Int8Node n: return n.ReadValueFromMemory(memory).ToString();
				case Int16Node n: return n.ReadValueFromMemory(memory).ToString();
				case Int32Node n: return n.ReadValueFromMemory(memory).ToString();
				case Int64Node n: return n.ReadValueFromMemory(memory).ToString();
				case NIntNode n: return n.ReadValueFromMemory(memory).ToInt64().ToString();
				case UInt8Node n: return n.ReadValueFromMemory(memory).ToString();
				case UInt16Node n: return n.ReadValueFromMemory(memory).ToString();
				case UInt32Node n: return n.ReadValueFromMemory(memory).ToString();
				case UInt64Node n: return n.ReadValueFromMemory(memory).ToString();
				case NUIntNode n: return n.ReadValueFromMemory(memory).ToUInt64().ToString();
				case BoolNode _: return memory.ReadUInt8(offset) != 0 ? "true" : "false";
				case FloatNode n: return n.ReadValueFromMemory(memory).ToString("0.000");
				case DoubleNode n: return n.ReadValueFromMemory(memory).ToString("0.000");

				case EnumNode enumNode:
					return enumNode.GetTextRepresentation(memory);
				case BitFieldNode bitFieldNode:
					return bitFieldNode.ConvertValueToBitString(memory);

				case BaseMatrixNode matrixNode:
				{
					var count = matrixNode.MemorySize / matrixNode.ValueTypeSize;
					var values = Enumerable.Range(0, count).Select(i => memory.ReadFloat(offset + i * matrixNode.ValueTypeSize).ToString("0.000"));
					return $"({string.Join(", ", values)})";
				}

				case BaseTextNode textNode:
					// Decode the raw bytes: the string ends at its terminator.
					return $"\"{CutAtTerminator(textNode.Encoding.GetString(memory.ReadBytes(offset, textNode.MemorySize)))}\"";
				case BaseTextPtrNode textPtrNode:
				{
					var pointer = memory.ReadIntPtr(offset);
					var text = ReadRemoteString(pointer, textPtrNode.Encoding);
					return text == null ? FormatAddress(pointer) : $"{FormatAddress(pointer)} \"{text}\"";
				}

				case ArrayBufferNode bufferNode:
					bufferNode.UpdateCountFromField(memory);
					return $"{FormatAddress(memory.ReadIntPtr(offset))} [{bufferNode.Count}]";

				// Pointers are not followed.
				case PointerNode _:
				case BaseFunctionPtrNode _:
				case VirtualMethodTableNode _:
					return FormatAddress(memory.ReadIntPtr(offset));

				case BaseWrapperArrayNode arrayNode:
				{
					if (arrayNode.InnerNode == null || !IsInlineArray(arrayNode))
					{
						return $"[{arrayNode.Count}]";
					}

					var elementSize = arrayNode.InnerNode.MemorySize;
					var values = Enumerable.Range(0, arrayNode.Count)
						.Select(i => GetValueText(arrayNode.InnerNode, Shift(memory, offset + i * elementSize), AddOffset(baseAddress, offset + (long)i * elementSize)));
					return $"[{string.Join(", ", values)}]";
				}

				case ClassInstanceNode instanceNode:
					return $"{{{instanceNode.InnerNode?.Name}}}";
				case UnionNode _:
					return "{union}";
			}

			return string.Empty;
		}

		private static string CutAtTerminator(string text)
		{
			if (text == null)
			{
				return string.Empty;
			}

			var index = text.IndexOf('\0');
			return index >= 0 ? text.Substring(0, index) : text;
		}

		/// <summary>Reads a null terminated string from the target. Returns null if it can't be read.</summary>
		private string ReadRemoteString(IntPtr address, Encoding encoding)
		{
			if (reader == null || address.IsNull())
			{
				return null;
			}

			var buffer = new byte[MaxStringCharacterCount * encoding.GuessByteCountPerChar()];
			if (!reader.ReadRemoteMemoryIntoBuffer(address, ref buffer))
			{
				return null;
			}

			return CutAtTerminator(encoding.GetString(buffer));
		}
	}
}
