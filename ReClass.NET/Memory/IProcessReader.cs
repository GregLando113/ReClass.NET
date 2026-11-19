using System;
using System.Collections.Generic;
using ReClassNET.MemoryScanner;

namespace ReClassNET.Memory
{
	public interface IProcessReader : IRemoteMemoryReader
	{
		Section GetSectionToPointer(IntPtr address);

		Module GetModuleToPointer(IntPtr address);

		Module GetModuleByName(string name);

		/// <summary>Searchs for the byte pattern in the module with the given name.</summary>
		/// <param name="moduleName">The name of the module.</param>
		/// <param name="pattern">The pattern to search.</param>
		/// <returns>The address of the first match or <see cref="IntPtr.Zero"/> if the module or the pattern was not found.</returns>
		IntPtr FindPatternInModule(string moduleName, BytePattern pattern);

		bool EnumerateRemoteSectionsAndModules(out List<Section> sections, out List<Module> modules);
	}
}
