using NFluent;
using ReClassNET.MemoryScanner;
using Xunit;

namespace ReClass.NET_Tests.MemoryScanner
{
	public class PatternScannerTest
	{
		private static readonly byte[] data = { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x11, 0x22, 0xAB, 0xCD };

		[Theory]
		[InlineData("00 11", 0)]
		[InlineData("33 44 55", 3)]
		[InlineData("AB CD", 10)] // last possible offset
		[InlineData("CD", 11)]
		[InlineData("11 22", 1)] // first match wins
		[InlineData("11 22 AB", 8)]
		[InlineData("11 ?? 33", 1)]
		[InlineData("11 ? 33", 1)]
		[InlineData("11 ? AB", 8)]
		[InlineData("?? ?? 22 33", 0)]
		[InlineData("? ? 22 33", 0)]
		[InlineData("?? 22 AB ??", 8)]
		[InlineData("4? 55", 4)]
		[InlineData("?4 55", 4)]
		[InlineData("A? ?D", 10)]
		[InlineData("?? ??", 0)]
		[InlineData("00 11 22 33 44 55 66 77 11 22 AB CD", 0)]
		public void FindsPattern(string pattern, int expected)
		{
			Check.That(PatternScanner.FindPattern(BytePattern.Parse(pattern), data)).IsEqualTo(expected);
		}

		[Theory]
		[InlineData("FF")]
		[InlineData("22 11")]
		[InlineData("11 ?? 44")]
		[InlineData("CD ??")]
		[InlineData("AB CD EF")]
		[InlineData("5? 77")]
		[InlineData("00 11 22 33 44 55 66 77 11 22 AB CD EF")]
		[InlineData("")]
		public void DoesNotFindPattern(string pattern)
		{
			Check.That(PatternScanner.FindPattern(BytePattern.Parse(pattern), data)).IsEqualTo(-1);
		}

		[Fact]
		public void EmptyDataIsNotMatched()
		{
			Check.That(PatternScanner.FindPattern(BytePattern.Parse("00"), new byte[0])).IsEqualTo(-1);
		}
	}
}
