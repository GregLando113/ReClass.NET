namespace ReClassNET.DataExchange.ReClass
{
	public partial class ReClassNetFile
	{
		public const string FormatName = "ReClass.NET File";
		public const string FileExtension = ".rcnet";
		public const string FileExtensionId = "rcnetfile";

		/// <summary>The extension of the uncompressed variant, which is the plain content of the data file of a <see cref="FileExtension"/> archive.</summary>
		public const string UncompressedFormatName = "ReClass.NET XML File";
		public const string UncompressedFileExtension = ".rcnet.xml";

		/// <summary>Checks if the path names an uncompressed ReClass.NET file (<see cref="UncompressedFileExtension"/>).</summary>
		public static bool IsUncompressedFilePath(string path)
		{
			return path != null && path.EndsWith(UncompressedFileExtension, System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Checks if the path names a ReClass.NET file, either compressed or uncompressed.</summary>
		public static bool IsFilePath(string path)
		{
			return path != null && (path.EndsWith(FileExtension, System.StringComparison.OrdinalIgnoreCase) || IsUncompressedFilePath(path));
		}

		/// <summary>Checks if the stream starts with the signature of a zip archive. The position of the stream is not changed.</summary>
		private static bool IsCompressed(System.IO.Stream input)
		{
			if (!input.CanSeek)
			{
				// Only the compressed format is exchanged over streams which can't seek.
				return true;
			}

			var position = input.Position;
			var first = input.ReadByte();
			var second = input.ReadByte();
			input.Position = position;

			return first == 'P' && second == 'K';
		}

		private const uint FileVersion = 0x00010001;
		private const uint FileVersionCriticalMask = 0xFFFF0000;

		private const string DataFileName = "Data.xml";

		private const string SerializationClassName = "__Serialization_Class__";

		public const string XmlRootElement = "reclass";
		public const string XmlCustomDataElement = "custom_data";
		public const string XmlTypeMappingElement = "type_mapping";
		public const string XmlEnumsElement = "enums";
		public const string XmlEnumElement = "enum";
		public const string XmlClassesElement = "classes";
		public const string XmlClassElement = "class";
		public const string XmlNodeElement = "node";
		public const string XmlMethodElement = "method";
		public const string XmlVersionAttribute = "version";
		public const string XmlPlatformAttribute = "type";
		public const string XmlUuidAttribute = "uuid";
		public const string XmlNameAttribute = "name";
		public const string XmlCommentAttribute = "comment";
		public const string XmlHiddenAttribute = "hidden";
		public const string XmlAddressAttribute = "address";
		public const string XmlPinnedAttribute = "pinned";
		public const string XmlTypeAttribute = "type";
		public const string XmlReferenceAttribute = "reference";
		public const string XmlCountAttribute = "count";
		public const string XmlCountFieldAttribute = "countField";
		public const string XmlTemplatesElement = "templates";
		public const string XmlTemplateElement = "template";
		public const string XmlArgumentElement = "argument";
		public const string XmlParameterAttribute = "param";
		public const string XmlBitsAttribute = "bits";
		public const string XmlLengthAttribute = "length";
		public const string XmlSizeAttribute = "size";
		public const string XmlSignatureAttribute = "signature";
		public const string XmlFlagsAttribute = "flags";
		public const string XmlItemElement = "item";
		public const string XmlValueAttribute = "value";
	}
}
