using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.Xml.Linq;
using ReClassNET.UI.Theming;

namespace ReClassNET.Util
{
	internal sealed class SettingsSerializer
	{
		private const string XmlRootElement = "Settings";
		private const string XmlGeneralElement = "General";
		private const string XmlDisplayElement = "Display";
		private const string XmlColorsElement = "Colors";
		private const string XmlDarkColorsElement = "DarkColors";
		private const string XmlCustomDataElement = "CustomData";

		#region Read Settings

		public static Settings Load()
		{
			EnsureSettingsDirectoryAvailable();

			var settings = new Settings();

			try
			{
				var path = Path.Combine(PathUtil.SettingsFolderPath, Constants.SettingsFile);

				using var sr = new StreamReader(path);

				var document = XDocument.Load(sr);
				var root = document.Root;

				var general = root?.Element(XmlGeneralElement);
				if (general != null)
				{
					XElementSerializer.TryRead(general, nameof(settings.LastProcess), e => settings.LastProcess = XElementSerializer.ToString(e));
					XElementSerializer.TryRead(general, nameof(settings.StayOnTop), e => settings.StayOnTop = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(general, nameof(settings.RunAsAdmin), e => settings.RunAsAdmin = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(general, nameof(settings.RandomizeWindowTitle), e => settings.RandomizeWindowTitle = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(general, nameof(settings.Theme), e => settings.Theme = Enum.TryParse<ThemeMode>(XElementSerializer.ToString(e), out var theme) ? theme : ThemeMode.Light);
				}
				var display = root?.Element(XmlDisplayElement);
				if (display != null)
				{
					XElementSerializer.TryRead(display, nameof(settings.ShowNodeAddress), e => settings.ShowNodeAddress = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowNodeOffset), e => settings.ShowNodeOffset = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowNodeText), e => settings.ShowNodeText = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.HighlightChangedValues), e => settings.HighlightChangedValues = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowCommentFloat), e => settings.ShowCommentFloat = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowCommentInteger), e => settings.ShowCommentInteger = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowCommentPointer), e => settings.ShowCommentPointer = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowCommentRtti), e => settings.ShowCommentRtti = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowCommentSymbol), e => settings.ShowCommentSymbol = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowCommentString), e => settings.ShowCommentString = XElementSerializer.ToBool(e));
					XElementSerializer.TryRead(display, nameof(settings.ShowCommentPluginInfo), e => settings.ShowCommentPluginInfo = XElementSerializer.ToBool(e));
				}
				ReadColors(root?.Element(XmlColorsElement), settings.LightColors);
				ReadColors(root?.Element(XmlDarkColorsElement), settings.DarkColors);
				var customData = root?.Element(XmlCustomDataElement);
				if (customData != null)
				{
					settings.CustomData.Deserialize(customData);
				}
			}
			catch
			{
				// ignored
			}

			return settings;
		}

		#endregion

		#region Write Settings

		public static void Save(Settings settings)
		{
			Contract.Requires(settings != null);

			EnsureSettingsDirectoryAvailable();

			var path = Path.Combine(PathUtil.SettingsFolderPath, Constants.SettingsFile);

			using var sw = new StreamWriter(path);

			var document = new XDocument(
				new XComment($"{Constants.ApplicationName} {Constants.ApplicationVersion} by {Constants.Author}"),
				new XComment($"Website: {Constants.HomepageUrl}"),
				new XElement(
					XmlRootElement,
					new XElement(
						XmlGeneralElement,
						XElementSerializer.ToXml(nameof(settings.LastProcess), settings.LastProcess),
						XElementSerializer.ToXml(nameof(settings.StayOnTop), settings.StayOnTop),
						XElementSerializer.ToXml(nameof(settings.RunAsAdmin), settings.RunAsAdmin),
						XElementSerializer.ToXml(nameof(settings.RandomizeWindowTitle), settings.RandomizeWindowTitle),
						XElementSerializer.ToXml(nameof(settings.Theme), settings.Theme.ToString())
					),
					new XElement(
						XmlDisplayElement,
						XElementSerializer.ToXml(nameof(settings.ShowNodeAddress), settings.ShowNodeAddress),
						XElementSerializer.ToXml(nameof(settings.ShowNodeOffset), settings.ShowNodeOffset),
						XElementSerializer.ToXml(nameof(settings.ShowNodeText), settings.ShowNodeText),
						XElementSerializer.ToXml(nameof(settings.HighlightChangedValues), settings.HighlightChangedValues),
						XElementSerializer.ToXml(nameof(settings.ShowCommentFloat), settings.ShowCommentFloat),
						XElementSerializer.ToXml(nameof(settings.ShowCommentInteger), settings.ShowCommentInteger),
						XElementSerializer.ToXml(nameof(settings.ShowCommentPointer), settings.ShowCommentPointer),
						XElementSerializer.ToXml(nameof(settings.ShowCommentRtti), settings.ShowCommentRtti),
						XElementSerializer.ToXml(nameof(settings.ShowCommentSymbol), settings.ShowCommentSymbol),
						XElementSerializer.ToXml(nameof(settings.ShowCommentString), settings.ShowCommentString),
						XElementSerializer.ToXml(nameof(settings.ShowCommentPluginInfo), settings.ShowCommentPluginInfo)
					),
					WriteColors(XmlColorsElement, settings.LightColors),
					WriteColors(XmlDarkColorsElement, settings.DarkColors),
					settings.CustomData.Serialize(XmlCustomDataElement)
				)
			);

			document.Save(sw);
		}

		#endregion

		#region Colors

		private static void ReadColors(XElement element, NodeColorScheme colors)
		{
			if (element == null)
			{
				return;
			}

			XElementSerializer.TryRead(element, "BackgroundColor", e => colors.Background = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "SelectedColor", e => colors.Selected = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "HiddenColor", e => colors.Hidden = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "OffsetColor", e => colors.Offset = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "AddressColor", e => colors.Address = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "HexColor", e => colors.Hex = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "TypeColor", e => colors.Type = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "NameColor", e => colors.Name = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "ValueColor", e => colors.Value = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "IndexColor", e => colors.Index = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "CommentColor", e => colors.Comment = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "TextColor", e => colors.Text = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "VTableColor", e => colors.VTable = XElementSerializer.ToColor(e));
			XElementSerializer.TryRead(element, "PluginColor", e => colors.Plugin = XElementSerializer.ToColor(e));
		}

		private static XElement WriteColors(string name, NodeColorScheme colors)
		{
			return new XElement(
				name,
				XElementSerializer.ToXml("BackgroundColor", colors.Background),
				XElementSerializer.ToXml("SelectedColor", colors.Selected),
				XElementSerializer.ToXml("HiddenColor", colors.Hidden),
				XElementSerializer.ToXml("OffsetColor", colors.Offset),
				XElementSerializer.ToXml("AddressColor", colors.Address),
				XElementSerializer.ToXml("HexColor", colors.Hex),
				XElementSerializer.ToXml("TypeColor", colors.Type),
				XElementSerializer.ToXml("NameColor", colors.Name),
				XElementSerializer.ToXml("ValueColor", colors.Value),
				XElementSerializer.ToXml("IndexColor", colors.Index),
				XElementSerializer.ToXml("CommentColor", colors.Comment),
				XElementSerializer.ToXml("TextColor", colors.Text),
				XElementSerializer.ToXml("VTableColor", colors.VTable),
				XElementSerializer.ToXml("PluginColor", colors.Plugin)
			);
		}

		#endregion

		private static void EnsureSettingsDirectoryAvailable()
		{
			try
			{
				if (Directory.Exists(PathUtil.SettingsFolderPath) == false)
				{
					Directory.CreateDirectory(PathUtil.SettingsFolderPath);
				}
			}
			catch
			{
				// ignored
			}
		}
	}
}
