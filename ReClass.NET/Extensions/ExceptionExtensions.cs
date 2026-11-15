using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

namespace ReClassNET.Extensions
{
	public static class ExceptionExtensions
	{
		/// <summary>
		/// Gets the messages of the exception and all of its inner exceptions, one per line.
		/// </summary>
		public static string GetMessageChain(this Exception ex)
		{
			Contract.Requires(ex != null);

			var messages = new List<string>();
			for (var current = ex; current != null; current = current.InnerException)
			{
				messages.Add(current.Message);
			}

			return string.Join(Environment.NewLine, messages);
		}
	}
}
