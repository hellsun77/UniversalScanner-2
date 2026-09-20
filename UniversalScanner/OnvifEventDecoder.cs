using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UniversalScanner
{

public static class OnvifEventDecoder
{
	private static readonly XNamespace NsWsnt = "http://docs.oasis-open.org/wsn/b-2";

	private static readonly XNamespace NsTt = "http://www.onvif.org/ver10/schema";

	private static readonly XNamespace NsTev = "http://www.onvif.org/ver10/events/wsdl";

	private static readonly Dictionary<string, string> EventTopicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "tns1:VideoSource/MotionAlarm", "Motion Alarm" },
		{ "tns1:RuleEngine/CellMotionDetector/Motion", "Cell Motion Detection" },
		{ "tns1:RuleEngine/LineDetector/Crossed", "Line Crossing" },
		{ "tns1:RuleEngine/FieldDetector/ObjectsInside", "Field Detection (Objects Inside)" },
		{ "tns1:RuleEngine/FieldDetector/Entered", "Intrusion / Field Detection" },
		{ "tns1:RuleEngine/TamperDetector/Tamper", "Tamper Detection" },
		{ "tns1:VideoSource/SignalLoss", "Video Signal Loss" },
		{ "tns1:VideoSource/GlobalSceneChange/ImagingService", "Scene Change" },
		{ "tns1:Device/Trigger/DigitalInput", "Digital Input" },
		{ "tns1:Device/Trigger/Relay", "Relay Output" },
		{ "tns1:Device/HardwareFailure/StorageFailure", "Storage Failure" },
		{ "tns1:AudioAnalytics/Audio/DetectedSound", "Audio Detection" },
		{ "tns1:VideoAnalytics/tns1:FaceDetect", "Face Detection" },
		{ "tns1:Monitoring/ProcessorUsage", "Processor Usage" },
		{ "tns1:Monitoring/OperatingTime/LastReset", "Last Reset" },
		{ "tns1:Monitoring/OperatingTime/LastReboot", "Last Reboot" },
		{ "tns1:Monitoring/OperatingTime/LastClockSynchronization", "Clock Sync" },
		{ "tns1:UserAlarm/tns1:Recording", "Recording" },
		{ "RuleEngine/HoneywellVA/IntrusionDetection", "HoneywellVA Intrusion Detection" },
		{ "RuleEngine/HoneywellVA/SmartMotionDetection", "HoneywellVA Smart Motion Detection" },
		{ "RuleEngine/HoneywellVA/LineCrossingDetection", "HoneywellVA Line Crossing" },
		{ "RuleEngine/HoneywellVA/LoiteringDetection", "HoneywellVA Loitering Detection" },
		{ "RuleEngine/HoneywellVA/FaceDetection", "HoneywellVA Face Detection" },
		{ "RuleEngine/HoneywellVA/PersonnelCount", "HoneywellVA Personnel Count" },
		{ "RuleEngine/HoneywellVA/ObjectRemoved", "HoneywellVA Object Removed" },
		{ "RuleEngine/HoneywellVA/ObjectLeftUnattended", "HoneywellVA Object Left Unattended" }
	};

	public static string DecodeEventName(string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return "[Empty Topic]";
		}
		string text = NormalizeTopic(topic);
		if (EventTopicMap.TryGetValue(text, out var value))
		{
			return value;
		}
		foreach (KeyValuePair<string, string> item in EventTopicMap)
		{
			if (text.IndexOf(item.Key, StringComparison.OrdinalIgnoreCase) >= 0 || item.Key.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return item.Value;
			}
		}
		if (text.IndexOf("HoneywellVA", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "Unknown HoneywellVA Event";
		}
		return topic;
	}

	public static string NormalizeTopic(string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return string.Empty;
		}
		return Regex.Replace(topic.Trim().TrimStart('/').TrimEnd('/'), "/+", "/");
	}

	public static OnvifEvent ParseNotificationMessage(XElement notification)
	{
		OnvifEvent onvifEvent = new OnvifEvent();
		onvifEvent.ArrivalTime = DateTime.Now;
		onvifEvent.RawXml = notification.ToString();
		XElement xElement = FindElement(notification, "Topic", NsWsnt);
		if (xElement != null)
		{
			string text = xElement.Value?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(text))
			{
				XElement xElement2 = xElement.Elements().FirstOrDefault();
				if (xElement2 != null)
				{
					text = xElement2.Name.LocalName;
				}
			}
			onvifEvent.Topic = text;
		}
		XElement xElement3 = FindInnerMessage(notification);
		if (xElement3 != null)
		{
			XAttribute xAttribute = xElement3.Attribute("PropertyOperation");
			if (xAttribute != null)
			{
				onvifEvent.PropertyOperation = xAttribute.Value.Trim();
			}
			XAttribute xAttribute2 = xElement3.Attribute("UtcTime");
			if (xAttribute2 != null && DateTime.TryParse(xAttribute2.Value, null, DateTimeStyles.RoundtripKind, out var result))
			{
				onvifEvent.ArrivalTime = result.ToLocalTime();
			}
			onvifEvent.Source = ReadSimpleItems(xElement3, "Source");
			onvifEvent.Key = ReadSimpleItems(xElement3, "Key");
			onvifEvent.Data = ReadSimpleItems(xElement3, "Data");
		}
		else
		{
			onvifEvent.Source = ReadSimpleItems(notification, "Source");
			onvifEvent.Key = ReadSimpleItems(notification, "Key");
			onvifEvent.Data = ReadSimpleItems(notification, "Data");
		}
		string text2 = DecodeEventName(onvifEvent.Topic);
		string text3 = (string.IsNullOrEmpty(onvifEvent.PropertyOperation) ? "" : (" [" + onvifEvent.PropertyOperation + "]"));
		string text4 = (string.IsNullOrEmpty(onvifEvent.Data) ? "" : (" → " + onvifEvent.Data));
		onvifEvent.Message = text2 + text3 + text4;
		List<string> list = new List<string>();
		if (!string.IsNullOrEmpty(onvifEvent.Source))
		{
			list.Add("Source: " + onvifEvent.Source);
		}
		if (!string.IsNullOrEmpty(onvifEvent.Key))
		{
			list.Add("Key: " + onvifEvent.Key);
		}
		if (!string.IsNullOrEmpty(onvifEvent.Data))
		{
			list.Add("Data: " + onvifEvent.Data);
		}
		onvifEvent.Details = string.Join(" | ", list);
		return onvifEvent;
	}

	public static bool IsMotionActive(OnvifEvent evt)
	{
		if (evt == null || string.IsNullOrEmpty(evt.Data))
		{
			return false;
		}
		string data = evt.Data;
		if (ContainsKeyValue(data, "IsMotion", "true") || ContainsKeyValue(data, "IsMotion", "1"))
		{
			return true;
		}
		if (ContainsKeyValue(data, "State", "true") || ContainsKeyValue(data, "State", "1"))
		{
			return true;
		}
		if (ContainsKeyValue(data, "LogicalState", "true") || ContainsKeyValue(data, "LogicalState", "1"))
		{
			return true;
		}
		return false;
	}

	public static bool IsMotionInactive(OnvifEvent evt)
	{
		if (evt == null || string.IsNullOrEmpty(evt.Data))
		{
			return false;
		}
		string data = evt.Data;
		if (ContainsKeyValue(data, "IsMotion", "false") || ContainsKeyValue(data, "IsMotion", "0"))
		{
			return true;
		}
		if (ContainsKeyValue(data, "State", "false") || ContainsKeyValue(data, "State", "0"))
		{
			return true;
		}
		if (ContainsKeyValue(data, "LogicalState", "false") || ContainsKeyValue(data, "LogicalState", "0"))
		{
			return true;
		}
		return false;
	}

	private static XElement FindInnerMessage(XElement notification)
	{
		XElement xElement = FindElement(notification, "Message", NsWsnt);
		if (xElement != null)
		{
			XElement xElement2 = FindElement(xElement, "Message", NsTt);
			if (xElement2 != null)
			{
				return xElement2;
			}
			if (xElement.Attribute("PropertyOperation") != null || xElement.Attribute("UtcTime") != null || FindElement(xElement, "Source", NsTt) != null || FindElement(xElement, "Data", NsTt) != null)
			{
				return xElement;
			}
			XElement xElement3 = xElement.Elements().FirstOrDefault((XElement e) => e.Name.LocalName == "Message");
			if (xElement3 != null)
			{
				return xElement3;
			}
			if (xElement.Descendants().Any((XElement d) => d.Name.LocalName == "SimpleItem"))
			{
				return xElement;
			}
		}
		XElement xElement4 = FindElement(notification, "Message", NsTt);
		if (xElement4 != null)
		{
			return xElement4;
		}
		return notification.Descendants().FirstOrDefault((XElement e) => e.Name.LocalName == "Message" && (e.Attribute("PropertyOperation") != null || e.Elements().Any((XElement c) => c.Name.LocalName == "Source" || c.Name.LocalName == "Data" || c.Name.LocalName == "Key")));
	}

	private static string ReadSimpleItems(XElement parent, string containerName)
	{
		if (parent == null)
		{
			return string.Empty;
		}
		XElement xElement = FindElement(parent, containerName, NsTt);
		if (xElement == null)
		{
			xElement = FindElement(parent, containerName, NsWsnt);
		}
		if (xElement == null)
		{
			xElement = parent.Elements().FirstOrDefault((XElement e) => e.Name.LocalName.Equals(containerName, StringComparison.OrdinalIgnoreCase));
		}
		if (xElement == null)
		{
			return string.Empty;
		}
		List<XElement> list = (from e in xElement.Elements()
			where e.Name.LocalName.Equals("SimpleItem", StringComparison.OrdinalIgnoreCase)
			select e).ToList();
		if (list.Count == 0)
		{
			return string.Empty;
		}
		List<string> list2 = new List<string>();
		foreach (XElement item in list)
		{
			string text = item.Attribute("Name")?.Value ?? "";
			string text2 = item.Attribute("Value")?.Value ?? "";
			if (!string.IsNullOrEmpty(text))
			{
				list2.Add(text + "=" + text2);
			}
			else if (!string.IsNullOrEmpty(text2))
			{
				list2.Add(text2);
			}
		}
		return string.Join("; ", list2);
	}

	private static XElement FindElement(XElement parent, string localName, XNamespace ns)
	{
		if (parent == null)
		{
			return null;
		}
		XElement xElement = parent.Element(ns + localName);
		if (xElement != null)
		{
			return xElement;
		}
		return parent.Elements().FirstOrDefault((XElement e) => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
	}

	private static bool ContainsKeyValue(string data, string key, string value)
	{
		if (string.IsNullOrEmpty(data))
		{
			return false;
		}
		string[] array = data.Split(';');
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			int num = text.IndexOf('=');
			if (num > 0)
			{
				string text2 = text.Substring(0, num).Trim();
				string text3 = text.Substring(num + 1).Trim();
				if (text2.Equals(key, StringComparison.OrdinalIgnoreCase) && text3.Equals(value, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		return false;
	}
}






public class OnvifEvent
{
	public DateTime ArrivalTime { get; set; } = DateTime.Now;


	public string Topic { get; set; } = string.Empty;


	public string PropertyOperation { get; set; } = string.Empty;


	public string Source { get; set; } = string.Empty;


	public string Key { get; set; } = string.Empty;


	public string Data { get; set; } = string.Empty;


	public string Message { get; set; } = string.Empty;


	public string Details { get; set; } = string.Empty;


	public string RawXml { get; set; } = string.Empty;

}

}
