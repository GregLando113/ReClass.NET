using System;
using NFluent;
using ReClassNET.AddressParser;
using Xunit;

namespace ReClass.NET_Tests.AddressParser
{
	public class ParserTest
	{
		[Theory]
		[InlineData("-")]
		[InlineData("+")]
		[InlineData("*")]
		[InlineData("/")]
		[InlineData(",")]
		[InlineData("(")]
		[InlineData(")")]
		[InlineData("[")]
		[InlineData("]")]
		[InlineData("1-")]
		[InlineData("1(")]
		[InlineData("1)")]
		[InlineData("1[")]
		[InlineData("1]")]
		[InlineData("(1")]
		[InlineData(")1")]
		[InlineData("[1")]
		[InlineData("]1")]
		[InlineData("1+(")]
		[InlineData("1+)")]
		[InlineData("1 + ()")]
		[InlineData("(1 + 2")]
		[InlineData("1 + 2)")]
		[InlineData("[1 + 2)")]
		[InlineData("(1 + 2]")]
		[InlineData("[1,")]
		[InlineData("[1,]")]
		[InlineData("[1,2]")]
		[InlineData("1,")]
		[InlineData("1,2")]
		public void InvalidExpressionTests(string expression)
		{
			Check.ThatCode(() => Parser.Parse(expression)).Throws<ParseException>();
		}

		[Theory]
		[InlineData("1", typeof(ConstantExpression))]
		[InlineData("1 + 2", typeof(AddExpression))]
		[InlineData("1 - 2", typeof(SubtractExpression))]
		[InlineData("1 * 2", typeof(MultiplyExpression))]
		[InlineData("1 / 2", typeof(DivideExpression))]
		[InlineData("1 + 2 * 3", typeof(AddExpression))]
		[InlineData("(1 + 2) * 3", typeof(MultiplyExpression))]
		[InlineData("1 + (2 * 3)", typeof(AddExpression))]
		[InlineData("(1 + (2 * 3))", typeof(AddExpression))]
		[InlineData("[1]", typeof(ReadMemoryExpression))]
		[InlineData("[1,4]", typeof(ReadMemoryExpression))]
		[InlineData("[1,8]", typeof(ReadMemoryExpression))]
		[InlineData("<test>", typeof(ModuleExpression))]
		[InlineData("[<test>]", typeof(ReadMemoryExpression))]
		public void ValidExpressionTests(string expression, Type type)
		{
			Check.That(Parser.Parse(expression)).IsInstanceOfType(type);
		}

		[Theory]
		[InlineData("<test.exe:AA>", "test.exe", "AA")]
		[InlineData("<test.exe:FF 35 ?? ?? ?? ?? E8>", "test.exe", "FF 35 ?? ?? ?? ?? E8")]
		[InlineData("<test.exe:FF 35 ? ? ? ? E8>", "test.exe", "FF 35 ?? ?? ?? ?? E8")]
		[InlineData("<test.exe:ff35????e8>", "test.exe", "FF 35 ?? ?? E8")]
		[InlineData("< test.exe : A? ?B ?? >", "test.exe", "A? ?B ??")]
		public void SignatureExpressionTests(string expression, string moduleName, string pattern)
		{
			var signatureExpression = (SignatureExpression)Parser.Parse(expression);

			Check.That(signatureExpression.ModuleName).IsEqualTo(moduleName);
			Check.That(signatureExpression.Pattern.ToString()).IsEqualTo(pattern);
		}

		[Theory]
		[InlineData("[<test.exe:FF 35 ?? ?? ?? ?? E8> + 2]", typeof(ReadMemoryExpression))]
		[InlineData("<test.exe:FF ?? E8> + 2", typeof(AddExpression))]
		public void SignatureInExpressionTests(string expression, Type type)
		{
			Check.That(Parser.Parse(expression)).IsInstanceOfType(type);
		}

		[Theory]
		[InlineData("<test.exe:>")]
		[InlineData("<test.exe: >")]
		[InlineData("<:AA>")]
		[InlineData("< :AA>")]
		[InlineData("<test.exe:ZZ>")]
		[InlineData("<test.exe:AA,BB>")]
		[InlineData("<test.exe:0xAA>")]
		[InlineData("<test.exe:??>")]
		[InlineData("<test.exe:? ?? ?>")]
		[InlineData("<test.exe:AA")]
		public void InvalidSignatureTests(string expression)
		{
			Check.ThatCode(() => Parser.Parse(expression)).Throws<ParseException>();
		}

		[Fact]
		public void ReadMemoryDefaultByteCountCheck()
		{
			var expression = (ReadMemoryExpression)Parser.Parse("[1]");

			Check.That(expression.ByteCount).IsEqualTo(IntPtr.Size);
		}
	}
}
