using System;
using System.Drawing;
using System.Linq;
using ReClassNET.Controls;
using ReClassNET.Extensions;
using ReClassNET.Logger;
using ReClassNET.Memory;
using ReClassNET.UI;

namespace ReClassNET.Nodes
{
	/// <summary>
	/// A pointer to a buffer of elements (<c>T* data</c>). The element count is either fixed or read from an integer field
	/// in the same container (e.g. <c>struct { T* data; uint32_t cap; uint32_t len; }</c>).
	/// Only the selected element gets read from the remote process.
	/// </summary>
	public class ArrayBufferNode : BaseWrapperArrayNode
	{
		private readonly MemoryBuffer memory = new MemoryBuffer();

		private BaseNode countFieldNode;
		private bool isCountFieldResolved = true;

		public override int MemorySize => IntPtr.Size;

		protected override bool PerformCycleCheck => false;

		/// <summary>
		/// The name of the integer field in the same container which holds the element count or null if the count is fixed.
		/// </summary>
		public string CountFieldName { get; set; }

		public ArrayBufferNode()
		{
			IsReadOnly = false;
		}

		public override void GetUserInterfaceInfo(out string name, out Image icon)
		{
			name = "Array Buffer";
			icon = Properties.Resources.B16x16_Button_Pointer_Array;
		}

		public override void Initialize()
		{
			ChangeInnerNode(IntPtr.Size == 4 ? (BaseNode)new Hex32Node() : new Hex64Node());
		}

		public override int CalculateSizeForInner(int innerSize) => IntPtr.Size;

		protected override int CalculateSizeForCount(int count) => IntPtr.Size;

		/// <summary>Tests if the node is an integer node which can be used as element count.</summary>
		public static bool IsCountFieldType(BaseNode node)
		{
			switch (node)
			{
				case Int8Node _:
				case Int16Node _:
				case Int32Node _:
				case Int64Node _:
				case UInt8Node _:
				case UInt16Node _:
				case UInt32Node _:
				case UInt64Node _:
				case NIntNode _:
				case NUIntNode _:
					return true;
			}
			return false;
		}

		/// <summary>Checks if the count of this buffer can be bound to the field <paramref name="name"/>.</summary>
		/// <param name="name">The name of the field.</param>
		/// <param name="reason">[out] The reason if the field can't be used.</param>
		/// <returns>True if the field can be used, false otherwise.</returns>
		public bool CanBindCountField(string name, out string reason)
		{
			reason = null;

			if (!(ParentNode is BaseContainerNode container))
			{
				reason = $"The array buffer '{Name}' must be a direct member of a class or union to use a count field.";
				return false;
			}

			var field = container.Nodes.FirstOrDefault(n => n != this && n.Name == name);
			if (field == null)
			{
				reason = $"There is no field named '{name}' next to the array buffer '{Name}'.";
				return false;
			}

			if (!IsCountFieldType(field))
			{
				reason = $"The field '{name}' must be an integer to be used as count of the array buffer '{Name}'.";
				return false;
			}

			return true;
		}

		/// <summary>Binds the count to the field <paramref name="name"/> if possible.</summary>
		/// <param name="name">The name of the field.</param>
		/// <param name="reason">[out] The reason if the field can't be used.</param>
		/// <returns>True if the field was bound, false otherwise.</returns>
		public bool BindCountField(string name, out string reason)
		{
			if (!CanBindCountField(name, out reason))
			{
				return false;
			}

			CountFieldName = name;
			countFieldNode = null;
			isCountFieldResolved = true;

			return true;
		}

		private BaseNode ResolveCountField()
		{
			if (!(ParentNode is BaseContainerNode container))
			{
				return null;
			}

			// Keep the binding if the field was renamed.
			if (countFieldNode != null && container.ContainsNode(countFieldNode) && IsCountFieldType(countFieldNode))
			{
				CountFieldName = countFieldNode.Name;

				return countFieldNode;
			}

			// The field may have been replaced (e.g. with another integer type) or the binding was just loaded.
			countFieldNode = container.Nodes.FirstOrDefault(n => n != this && n.Name == CountFieldName && IsCountFieldType(n));

			return countFieldNode;
		}

		/// <summary>
		/// Updates <see cref="BaseWrapperArrayNode.Count"/> from the bound count field.
		/// If the field can't be resolved, the last count is kept.
		/// </summary>
		/// <param name="containerMemory">The memory of the container which holds this node and the count field.</param>
		public void UpdateCountFromField(MemoryBuffer containerMemory)
		{
			if (CountFieldName == null)
			{
				isCountFieldResolved = true;
				return;
			}

			var field = ResolveCountField();
			if (field == null)
			{
				isCountFieldResolved = false;
				return;
			}

			isCountFieldResolved = true;

			long value;
			switch (field)
			{
				case Int8Node n: value = n.ReadValueFromMemory(containerMemory); break;
				case Int16Node n: value = n.ReadValueFromMemory(containerMemory); break;
				case Int32Node n: value = n.ReadValueFromMemory(containerMemory); break;
				case Int64Node n: value = n.ReadValueFromMemory(containerMemory); break;
				case UInt8Node n: value = n.ReadValueFromMemory(containerMemory); break;
				case UInt16Node n: value = n.ReadValueFromMemory(containerMemory); break;
				case UInt32Node n: value = n.ReadValueFromMemory(containerMemory); break;
				case UInt64Node n: value = (long)Math.Min(n.ReadValueFromMemory(containerMemory), int.MaxValue); break;
				case NIntNode n: value = n.ReadValueFromMemory(containerMemory).ToInt64(); break;
				case NUIntNode n: value = (long)Math.Min(n.ReadValueFromMemory(containerMemory).ToUInt64(), int.MaxValue); break;
				default: return;
			}

			Count = (int)Math.Max(0, Math.Min(value, int.MaxValue));

			CurrentIndex = Math.Max(0, Math.Min(CurrentIndex, Count - 1));
		}

		protected override string GetCountText()
		{
			if (CountFieldName == null)
			{
				return Count.ToString();
			}

			return isCountFieldResolved ? $"{CountFieldName}={Count}" : $"{CountFieldName}=?";
		}

		protected override string GetValueText(DrawContext context)
		{
			return "-> 0x" + context.Memory.ReadIntPtr(Offset).ToString(Constants.AddressHexFormat);
		}

		public override Size Draw(DrawContext context, int x, int y)
		{
			UpdateCountFromField(context.Memory);

			return Draw(context, x, y, "ArrayBuf", context.IconProvider.Pointer);
		}

		protected override Size DrawChild(DrawContext context, int x, int y)
		{
			var ptr = context.Memory.ReadIntPtr(Offset);
			if (Count == 0 || ptr.IsNull())
			{
				return Size.Empty;
			}

			var elementSize = InnerNode.MemorySize;

			var address = ptr.ToInt64() + (long)elementSize * CurrentIndex;
			var elementAddress = IntPtr.Size == 4 ? new IntPtr(unchecked((int)address)) : new IntPtr(address);

			memory.Size = elementSize;
			memory.UpdateFrom(context.Process, elementAddress);

			var innerContext = context.Clone();
			innerContext.Address = elementAddress;
			innerContext.Memory = memory;

			return InnerNode.Draw(innerContext, x, y);
		}

		public override int CalculateDrawnHeight(DrawContext context)
		{
			if (IsHidden && !IsWrapped)
			{
				return HiddenHeight;
			}

			var height = context.Font.Height;
			if (LevelsOpen[context.Level] && Count > 0)
			{
				height += InnerNode.CalculateDrawnHeight(context);
			}
			return height;
		}

		public override void Update(HotSpot spot)
		{
			var isNumber = int.TryParse(spot.Text, out var number);

			if (spot.Id == 0 && !IsReadOnly && !isNumber)
			{
				// A field name binds the count to that field ("len" or the displayed "len=5").
				var name = spot.Text.Split('=')[0].Trim();
				if (!BindCountField(name, out var reason))
				{
					Program.Logger.Log(LogLevel.Warning, reason);
				}

				return;
			}

			if (spot.Id == 0 && number > 0)
			{
				// A positive number switches back to a fixed count.
				CountFieldName = null;
				countFieldNode = null;
				isCountFieldResolved = true;
			}

			base.Update(spot);
		}
	}
}
