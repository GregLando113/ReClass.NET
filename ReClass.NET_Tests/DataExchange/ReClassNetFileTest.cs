using System.IO;
using System.Linq;
using System.Text;
using NFluent;
using ReClassNET.DataExchange.ReClass;
using ReClassNET.Logger;
using ReClassNET.Nodes;
using ReClassNET.Project;
using Xunit;

namespace ReClass.NET_Tests.DataExchange
{
	public class ReClassNetFileTest
	{
		private static ReClassNetProject CreateProject()
		{
			var project = new ReClassNetProject();

			var classNode = ClassNode.Create();
			classNode.Name = "Player";
			classNode.Comment = "The local player";
			project.AddClass(classNode);
			classNode.AddNodes(new BaseNode[] { new Int32Node { Name = "health" }, new FloatNode { Name = "speed" } });

			return project;
		}

		[Theory]
		[InlineData("Project.rcnet.xml", true)]
		[InlineData("Project.RCNET.XML", true)]
		[InlineData("Project.rcnet", false)]
		[InlineData("Project.xml", false)]
		[InlineData(null, false)]
		public void IsUncompressedFilePathChecksBothExtensionParts(string path, bool expected)
		{
			Check.That(ReClassNetFile.IsUncompressedFilePath(path)).IsEqualTo(expected);
		}

		[Theory]
		[InlineData("Project.rcnet.xml", true)]
		[InlineData("Project.rcnet", true)]
		[InlineData("Project.xml", false)]
		[InlineData("Project.reclass", false)]
		public void IsFilePathAcceptsBothFormats(string path, bool expected)
		{
			Check.That(ReClassNetFile.IsFilePath(path)).IsEqualTo(expected);
		}

		[Fact]
		public void UncompressedStreamIsPlainXmlAndLoadsAgain()
		{
			byte[] data;
			using (var output = new MemoryStream())
			{
				new ReClassNetFile(CreateProject()).SaveUncompressed(output, new NullLogger());
				data = output.ToArray();
			}

			var text = Encoding.UTF8.GetString(data);
			Check.That(text).Contains("<" + ReClassNetFile.XmlRootElement);
			Check.That(text).Contains("name=\"health\"");

			var loadedProject = new ReClassNetProject();
			using (var input = new MemoryStream(data))
			{
				new ReClassNetFile(loadedProject).Load(input, new NullLogger());
			}

			Check.That(loadedProject.Classes.Select(c => c.Name)).ContainsExactly("Player");
			Check.That(loadedProject.Classes[0].Nodes.Select(n => n.Name)).ContainsExactly("health", "speed");
		}

		[Theory]
		[InlineData("Project.rcnet.xml", false)]
		[InlineData("Project.rcnet", true)]
		public void SaveChoosesTheFormatFromThePath(string fileName, bool compressed)
		{
			var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
			Directory.CreateDirectory(directory);
			try
			{
				var path = Path.Combine(directory, fileName);

				new ReClassNetFile(CreateProject()).Save(path, new NullLogger());

				var data = File.ReadAllBytes(path);
				Check.That(data[0] == 'P' && data[1] == 'K').IsEqualTo(compressed);

				var loadedProject = new ReClassNetProject();
				new ReClassNetFile(loadedProject).Load(path, new NullLogger());

				Check.That(loadedProject.Classes.Select(c => c.Name)).ContainsExactly("Player");
			}
			finally
			{
				Directory.Delete(directory, true);
			}
		}
	}
}
