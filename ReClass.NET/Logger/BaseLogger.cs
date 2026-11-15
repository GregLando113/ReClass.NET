using System;
using System.Diagnostics.Contracts;
using ReClassNET.Extensions;

namespace ReClassNET.Logger
{
	public abstract class BaseLogger : ILogger
	{
		private readonly object sync = new object();

		public event NewLogEntryEventHandler NewLogEntry;

		public void Log(Exception ex)
		{
			Log(LogLevel.Error, ex.GetMessageChain(), ex);
		}

		public void Log(LogLevel level, string message)
		{
			Log(level, message, null);
		}

		private void Log(LogLevel level, string message, Exception ex)
		{
			Contract.Requires(message != null);

			lock (sync)
			{
				NewLogEntry?.Invoke(level, message, ex);
			}
		}
	}
}
