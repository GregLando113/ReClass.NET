using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using ReClassNET.Logger;
using ReClassNET.Nodes;
using ReClassNET.Project;

namespace ReClassNET.DataExchange.ReClass
{
	public partial class ReClassNetFile
	{
		public void Save(string filePath, ILogger logger)
		{
			using var fs = new FileStream(filePath, FileMode.Create);

			Save(fs, logger);
		}

		public void Save(Stream output, ILogger logger)
		{
			using var archive = new ZipArchive(output, ZipArchiveMode.Create);

			var dataEntry = archive.CreateEntry(DataFileName);
			using var entryStream = dataEntry.Open();

			var document = new XDocument(
				new XComment($"{Constants.ApplicationName} {Constants.ApplicationVersion} by {Constants.Author}"),
				new XComment($"Website: {Constants.HomepageUrl}"),
				new XElement(
					XmlRootElement,
					new XAttribute(XmlVersionAttribute, FileVersion),
					new XAttribute(XmlPlatformAttribute, Constants.Platform),
					project.CustomData.Serialize(XmlCustomDataElement),
					project.TypeMapping.Serialize(XmlTypeMappingElement),
					new XElement(XmlEnumsElement, CreateEnumElements(project.Enums)),
					project.Templates.Count == 0 ? null : new XElement(XmlTemplatesElement, CreateClassElements(project.Templates, logger, XmlTemplateElement)),
					new XElement(XmlClassesElement, CreateClassElements(project.Classes, logger))
				)
			);

			document.Save(entryStream);
		}

		private static IEnumerable<XElement> CreateEnumElements(IEnumerable<EnumDescription> enums)
		{
			return enums.Select(e => new XElement(
				XmlEnumElement,
				new XAttribute(XmlNameAttribute, e.Name),
				new XAttribute(XmlSizeAttribute, e.Size),
				new XAttribute(XmlFlagsAttribute, e.UseFlagsMode),
				e.Values.Select(kv => new XElement(
					XmlItemElement,
					new XAttribute(XmlNameAttribute, kv.Key),
					new XAttribute(XmlValueAttribute, kv.Value)
				))
			));
		}

		private static IEnumerable<XElement> CreateClassElements(IEnumerable<ClassNode> classes, ILogger logger, string elementName = XmlClassElement)
		{
			Contract.Requires(classes != null);
			Contract.Requires(Contract.ForAll(classes, c => c != null));
			Contract.Requires(logger != null);
			Contract.Ensures(Contract.Result<IEnumerable<XElement>>() != null);

			return classes.Select(c => new XElement(
				elementName,
				new XAttribute(XmlUuidAttribute, c.Uuid),
				new XAttribute(XmlNameAttribute, c.Name ?? string.Empty),
				new XAttribute(XmlCommentAttribute, c.Comment ?? string.Empty),
				new XAttribute(XmlAddressAttribute, c.AddressFormula ?? string.Empty),
				c.Nodes.Select(n => CreateElementFromNode(n, logger)).Where(e => e != null)
			));
		}

		internal static XElement CreateElementFromNode(BaseNode node, ILogger logger)
		{
			Contract.Requires(node != null);
			Contract.Requires(logger != null);

			XElement CreateElement()
			{
				var converter = CustomNodeSerializer.GetWriteConverter(node);
				if (converter != null)
				{
					return converter.CreateElementFromNode(node, logger, CreateElementFromNode);
				}

				if (!buildInTypeToStringMap.TryGetValue(node.GetType(), out var typeString))
				{
					logger.Log(LogLevel.Error, $"Skipping node with unknown type: {node.Name}");
					logger.Log(LogLevel.Warning, node.GetType().ToString());

					return null;
				}

				return new XElement(
					XmlNodeElement,
					new XAttribute(XmlTypeAttribute, typeString)
				);
			}

			var element = CreateElement();
			if (element == null)
			{
				logger.Log(LogLevel.Error, "Could not create element.");

				return null;
			}

			element.SetAttributeValue(XmlNameAttribute, node.Name ?? string.Empty);
			element.SetAttributeValue(XmlCommentAttribute, node.Comment ?? string.Empty);
			element.SetAttributeValue(XmlHiddenAttribute, node.IsHidden);

			if (node is TemplateImplementationNode implementationNode)
			{
				// The generated class is not saved, only the template and the arguments.
				if (implementationNode.Template != null)
				{
					element.SetAttributeValue(XmlReferenceAttribute, implementationNode.Template.Uuid);
				}

				foreach (var kv in implementationNode.Arguments)
				{
					var argumentElement = CreateElementFromNode(kv.Value, logger);
					if (argumentElement != null)
					{
						element.Add(new XElement(XmlArgumentElement, new XAttribute(XmlParameterAttribute, kv.Key), argumentElement));
					}
				}
			}
			else if (node is BaseWrapperNode wrapperNode)
			{
				if (node is BaseClassWrapperNode classWrapperNode)
				{
					element.SetAttributeValue(XmlReferenceAttribute, ((ClassNode)classWrapperNode.InnerNode).Uuid);
				}
				else if (wrapperNode.InnerNode != null)
				{
					element.Add(CreateElementFromNode(wrapperNode.InnerNode, logger));
				}
			}

			switch (node)
			{
				case VirtualMethodTableNode vtableNode:
				{
					element.Add(vtableNode.Nodes.Select(n => new XElement(
						XmlMethodElement,
						new XAttribute(XmlNameAttribute, n.Name ?? string.Empty),
						new XAttribute(XmlCommentAttribute, n.Comment ?? string.Empty),
						new XAttribute(XmlHiddenAttribute, n.IsHidden)
					)));
					break;
				}
				case UnionNode unionNode:
				{
					element.Add(unionNode.Nodes.Select(n => CreateElementFromNode(n, logger)));
					break;
				}
				case BaseWrapperArrayNode arrayNode:
				{
					element.SetAttributeValue(XmlCountAttribute, arrayNode.Count);
					if (arrayNode is ArrayBufferNode arrayBufferNode && arrayBufferNode.CountFieldName != null)
					{
						element.SetAttributeValue(XmlCountFieldAttribute, arrayBufferNode.CountFieldName);
					}
					break;
				}
				case TemplateParameterNode parameterNode:
				{
					element.SetAttributeValue(XmlParameterAttribute, parameterNode.ParameterName);
					break;
				}
				case BaseTextNode textNode:
				{
					element.SetAttributeValue(XmlLengthAttribute, textNode.Length);
					break;
				}
				case BitFieldNode bitFieldNode:
				{
					element.SetAttributeValue(XmlBitsAttribute, bitFieldNode.Bits);
					break;
				}
				case FunctionNode functionNode:
				{
					var uuid = functionNode.BelongsToClass?.Uuid ?? Guid.Empty;
					element.SetAttributeValue(XmlReferenceAttribute, uuid);
					element.SetAttributeValue(XmlSignatureAttribute, functionNode.Signature);
					break;
				}
				case EnumNode enumNode:
				{
					element.SetAttributeValue(XmlReferenceAttribute, enumNode.Enum.Name);
					break;
				}
			}

			return element;
		}

		public static void SerializeNodesToStream(Stream output, IEnumerable<BaseNode> nodes, ILogger logger)
		{
			Contract.Requires(output != null);
			Contract.Requires(nodes != null);
			Contract.Requires(Contract.ForAll(nodes, n => n != null));
			Contract.Requires(logger != null);

			using var project = new ReClassNetProject();

			void RecursiveAddClasses(BaseNode node)
			{
				ClassNode classNode = null;
				switch (node)
				{
					case TemplateImplementationNode implementationNode:
						// The template itself must exist in the target project. Only the classes used as arguments are added.
						foreach (var argument in implementationNode.Arguments.Values)
						{
							RecursiveAddClasses(argument);
						}
						return;
					case TemplateInstanceClassNode _:
						return;
					case ClassNode c1:
						classNode = c1;
						break;
					case BaseWrapperNode wrapperNode when wrapperNode.ResolveMostInnerNode() is ClassNode c2:
						classNode = c2;
						break;
				}

				if (classNode == null || project.ContainsClass(classNode.Uuid))
				{
					return;
				}

				project.AddClass(classNode);

				foreach (var wrapperNodeChild in classNode.Nodes.OfType<BaseWrapperNode>())
				{
					RecursiveAddClasses(wrapperNodeChild);
				}
			}

			var serialisationClass = new ClassNode(false)
			{
				Name = SerializationClassName
			};

			var needsSerialisationClass = true;

			foreach (var node in nodes)
			{
				RecursiveAddClasses(node);

				if (!(node is ClassNode))
				{
					if (needsSerialisationClass)
					{
						needsSerialisationClass = false;

						project.AddClass(serialisationClass);
					}

					serialisationClass.AddNode(node);
				}
			}

			var file = new ReClassNetFile(project);
			file.Save(output, logger);
		}
	}
}
