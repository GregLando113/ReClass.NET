using System.Runtime.CompilerServices;
using System.Text;

namespace ReClass.NET_Tests
{
	internal static class TestSetup
	{
		/// <summary>
		/// Code page encodings like Windows-1252 are not available by default.
		/// The application registers the provider at startup, the tests need to do the same.
		/// </summary>
		[ModuleInitializer]
		internal static void Initialize()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}
	}
}
