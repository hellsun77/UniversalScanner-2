using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace UniversalScanner
{

public class OnvifEventClient : IDisposable
{
	private readonly string _ipAddress;

	private readonly int _port;

	private readonly string _username;

	private readonly string _password;

	private readonly HttpClient _httpClient;

	private readonly bool _disposeHttpClient;

	private bool _disposed;

	private string _eventsServiceUrl;

	private string _subscriptionEndpoint;

	private DateTime _subscriptionExpiresAt;

	private Timer _renewalTimer;

	private static readonly XNamespace NsTev = "http://www.onvif.org/ver10/events/wsdl";

	private static readonly XNamespace NsTds = "http://www.onvif.org/ver10/device/wsdl";

	private static readonly XNamespace NsTt = "http://www.onvif.org/ver10/schema";

	private static readonly XNamespace NsWsnt = "http://docs.oasis-open.org/wsn/b-2";

	private static readonly XNamespace NsWsa = "http://www.w3.org/2005/08/addressing";

	private static readonly XNamespace NsWsa04 = "http://schemas.xmlsoap.org/ws/2004/08/addressing";

	public DateTime SubscriptionExpiresAt
	{
		get
		{
			return _subscriptionExpiresAt;
		}
		set
		{
			_subscriptionExpiresAt = value;
		}
	}

	public OnvifEventClient(string ipAddress, int port, string username, string password, HttpClient httpClient = null)
	{
		_ipAddress = ipAddress ?? throw new ArgumentNullException("ipAddress");
		_port = port;
		_username = username ?? string.Empty;
		_password = password ?? string.Empty;
		if (httpClient != null)
		{
			_httpClient = httpClient;
		}
		else
		{
			var handler = new HttpClientHandler
			{
				Credentials = new NetworkCredential(_username, _password),
				PreAuthenticate = true
			};
			_httpClient = new HttpClient(handler);
		}
		_disposeHttpClient = httpClient == null;
		_httpClient.Timeout = TimeSpan.FromSeconds(15.0);
	}

	public async Task<bool> HasEventsServiceAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		string deviceServiceUrl = GetDeviceServiceUrl();
		string text = await GetCapabilitiesAsync(deviceServiceUrl, cancellationToken);
		if (!string.IsNullOrEmpty(text))
		{
			XDocument xDocument = XDocument.Parse(text);
			string text2 = null;
			XElement xElement = xDocument.Descendants().FirstOrDefault((XElement e) => e.Name.LocalName == "Capabilities" && (e.Name.Namespace == NsTds || e.Name.Namespace == NsTt));
			if (xElement != null)
			{
				XElement xElement2 = xElement.Elements().FirstOrDefault((XElement e) => e.Name.LocalName == "Events" || e.Name.LocalName == "Event");
				if (xElement2 != null)
				{
					XElement xElement3 = xElement2.Elements().FirstOrDefault((XElement e) => e.Name.LocalName == "XAddr");
					if (xElement3 != null)
					{
						text2 = xElement3.Value?.Trim();
					}
				}
			}
			if (string.IsNullOrEmpty(text2))
			{
				XElement xElement4 = xDocument.Descendants().FirstOrDefault((XElement e) => (e.Name.LocalName == "Events" || e.Name.LocalName == "Event") && e.Elements().Any((XElement c) => c.Name.LocalName == "XAddr"));
				if (xElement4 != null)
				{
					XElement xElement5 = xElement4.Elements().FirstOrDefault((XElement e) => e.Name.LocalName == "XAddr");
					if (xElement5 != null)
					{
						text2 = xElement5.Value?.Trim();
					}
				}
			}
			if (!string.IsNullOrEmpty(text2))
			{
				_eventsServiceUrl = text2;
				return true;
			}
		}
		return false;
	}

	public async Task<List<string>> GetEventPropertiesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrEmpty(_eventsServiceUrl))
		{
			throw new InvalidOperationException("Events service URL not initialized. Call HasEventsServiceAsync first.");
		}
		string text = await PostSoapAsync(_eventsServiceUrl, GetEventPropertiesBody(), "http://www.onvif.org/ver10/events/wsdl/EventPortType/GetEventPropertiesRequest", cancellationToken);
		List<string> list = new List<string>();
		if (!string.IsNullOrEmpty(text))
		{
			foreach (XElement item2 in XDocument.Parse(text).Descendants())
			{
				if (!(item2.Name.LocalName == "TopicNamespace"))
				{
					continue;
				}
				XAttribute xAttribute = item2.Attribute("Location");
				if (xAttribute != null && !string.IsNullOrWhiteSpace(xAttribute.Value))
				{
					string item = xAttribute.Value.TrimEnd('/');
					if (!list.Contains(item))
					{
						list.Add(item);
					}
				}
			}
		}
		return list;
	}

	public async Task<string> CreatePullPointSubscriptionAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrEmpty(_eventsServiceUrl))
		{
			throw new InvalidOperationException("Events service URL not initialized. Call HasEventsServiceAsync first.");
		}
		string text = await PostSoapAsync(_eventsServiceUrl, CreatePullPointSubscriptionBody(), "http://www.onvif.org/ver10/events/wsdl/EventPortType/CreatePullPointSubscriptionRequest", cancellationToken);
		if (!string.IsNullOrEmpty(text))
		{
			XDocument doc = XDocument.Parse(text);
			string text2 = FindSubscriptionAddress(doc);
			if (!string.IsNullOrEmpty(text2))
			{
				_subscriptionEndpoint = text2;
				string text3 = FindElementValue(doc, "TerminationTime");
				if (!string.IsNullOrEmpty(text3) && DateTime.TryParse(text3, null, DateTimeStyles.RoundtripKind, out var result))
				{
					_subscriptionExpiresAt = result;
					SetupRenewalTimer();
				}
				else
				{
					_subscriptionExpiresAt = DateTime.UtcNow.AddHours(1.0);
					SetupRenewalTimer();
				}
			}
		}
		return _subscriptionEndpoint;
	}

	public async Task<bool> RenewSubscriptionAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrEmpty(_subscriptionEndpoint))
		{
			return false;
		}
		string text = await PostSoapAsync(_subscriptionEndpoint, RenewSubscriptionBody(), "http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/RenewRequest", cancellationToken);
		if (!string.IsNullOrEmpty(text))
		{
			XDocument doc = XDocument.Parse(text);
			string text2 = FindElementValue(doc, "TerminationTime");
			if (!string.IsNullOrEmpty(text2) && DateTime.TryParse(text2, null, DateTimeStyles.RoundtripKind, out var result))
			{
				_subscriptionExpiresAt = result;
				SetupRenewalTimer();
				return true;
			}
		}
		return false;
	}

	public async Task<List<OnvifEvent>> PullMessagesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrEmpty(_subscriptionEndpoint))
		{
			return new List<OnvifEvent>();
		}
		string text = await PostSoapAsync(_subscriptionEndpoint, PullMessagesBody(), "http://www.onvif.org/ver10/events/wsdl/PullPointSubscription/PullMessagesRequest", cancellationToken);
		List<OnvifEvent> list = new List<OnvifEvent>();
		if (!string.IsNullOrEmpty(text))
		{
			foreach (XElement item3 in (from e in XDocument.Parse(text).Descendants()
				where e.Name.LocalName == "NotificationMessage"
				select e).ToList())
			{
				try
				{
					OnvifEvent item = OnvifEventDecoder.ParseNotificationMessage(item3);
					list.Add(item);
				}
				catch (Exception)
				{
					OnvifEvent item2 = new OnvifEvent
					{
						ArrivalTime = DateTime.Now,
						RawXml = item3.ToString(),
						Topic = "[Parse Error]",
						Message = "Failed to parse notification"
					};
					list.Add(item2);
				}
			}
		}
		return list;
	}

	public async Task UnsubscribeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			if (!string.IsNullOrEmpty(_subscriptionEndpoint))
			{
				await PostSoapAsync(_subscriptionEndpoint, UnsubscribeBody(), "http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/UnsubscribeRequest", cancellationToken);
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			try
			{
				_renewalTimer?.Change(-1, -1);
			}
			catch
			{
			}
			_subscriptionEndpoint = null;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		if (!disposing)
		{
			return;
		}
		try
		{
			_renewalTimer?.Change(-1, -1);
		}
		catch
		{
		}
		try
		{
			_renewalTimer?.Dispose();
		}
		catch
		{
		}
		_renewalTimer = null;
		if (!_disposeHttpClient)
		{
			return;
		}
		try
		{
			_httpClient.Dispose();
		}
		catch
		{
		}
	}

	private string GetDeviceServiceUrl()
	{
		return $"http://{_ipAddress}:{_port}/onvif/device_service";
	}

	private async Task<string> GetCapabilitiesAsync(string url, CancellationToken cancellationToken)
	{
		return await PostSoapAsync(url, GetCapabilitiesBody(), "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", cancellationToken);
	}

	public void SetupRenewalTimer()
	{
		_renewalTimer?.Change(-1, -1);
		long num = Math.Max(30000L, (long)(_subscriptionExpiresAt - DateTime.UtcNow).TotalMilliseconds - 60000);
		if (num > 0)
		{
			_renewalTimer = new Timer(async delegate
			{
				try { await RenewSubscriptionAsync(); } catch { }
			}, null, num, -1L);
		}
	}

	private async Task<string> PostSoapAsync(string url, string bodyXml, string action, CancellationToken cancellationToken)
	{
		string content2 = UniversalScanner.DeviceSDK.ONVIF.OnvifSoapAuthHelper.WrapSoapWithAuth(bodyXml, action, url, _username, _password);
		try
		{
			using (StringContent content = new StringContent(content2, Encoding.UTF8, "application/soap+xml"))
			{
				HttpResponseMessage response = await _httpClient.PostAsync(url, content, cancellationToken);
				string responseXml = await response.Content.ReadAsStringAsync();
				
				if (!string.IsNullOrWhiteSpace(responseXml) && (responseXml.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0 || responseXml.IndexOf("NotAuthorized", StringComparison.OrdinalIgnoreCase) >= 0))
				{
					throw new InvalidOperationException($"Camera returned an ONVIF SOAP fault. Response: {responseXml}");
				}

				if (!response.IsSuccessStatusCode)
				{
					throw new InvalidOperationException($"HTTP error {(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseXml}");
				}
				
				return responseXml;
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			LogDiagnostic(_ipAddress, action, content2, ex, url);
			throw;
		}
	}

	private void LogDiagnostic(string ipAddress, string action, string requestBody, Exception ex, string endpointUrl)
	{
#if DEBUG
		try
		{
			string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OnvifEventMonitor_Diagnostics.txt");
			try { System.IO.File.OpenWrite(logPath).Close(); }
			catch { logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OnvifEventMonitor_Diagnostics.txt"); }
			
			string sanitizedReq = requestBody;
			if (sanitizedReq.Contains("Password"))
			{
				sanitizedReq = System.Text.RegularExpressions.Regex.Replace(sanitizedReq, @"<([A-Za-z0-9:]*)Password[^>]*>.*?</\1Password>", "<$1Password>[SANITIZED]</$1Password>");
			}

			string msg = $"\r\n--- ONVIF EVENT MONITOR DIAGNOSTIC SESSION ---\r\n" +
						 $"Timestamp: {DateTime.UtcNow:O}\r\n" +
						 $"Camera IP: {ipAddress}\r\n" +
						 $"Endpoint: {endpointUrl}\r\n" +
						 $"SOAPAction: {action}\r\n" +
						 $"Sanitized Request:\r\n{sanitizedReq}\r\n\r\n" +
						 $"Exception Type: {ex.GetType().Name}\r\n" +
						 $"Exception Message: {ex.Message}\r\n" +
						 $"Stack Trace:\r\n{ex.StackTrace}\r\n\r\n";

			System.IO.File.AppendAllText(logPath, msg);
		}
		catch { }
#endif
	}

	private string FindSubscriptionAddress(XDocument doc)
	{
		foreach (XElement item in from e in doc.Descendants()
			where e.Name.LocalName == "SubscriptionReference"
			select e)
		{
			XElement xElement = item.Elements().FirstOrDefault((XElement e) => e.Name.LocalName == "Address");
			if (xElement != null && !string.IsNullOrWhiteSpace(xElement.Value))
			{
				return xElement.Value.Trim();
			}
		}
		foreach (XElement item2 in from e in doc.Descendants()
			where e.Name.LocalName == "EndpointReference"
			select e)
		{
			XElement xElement2 = item2.Elements().FirstOrDefault((XElement e) => e.Name.LocalName == "Address");
			if (xElement2 != null && !string.IsNullOrWhiteSpace(xElement2.Value))
			{
				return xElement2.Value.Trim();
			}
		}
		return null;
	}

	private string FindElementValue(XDocument doc, string localName)
	{
		return doc.Descendants().FirstOrDefault((XElement e) => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
	}

	private string GetCapabilitiesBody()
	{
		return "<GetCapabilities xmlns=\"http://www.onvif.org/ver10/device/wsdl\"><Category>All</Category></GetCapabilities>";
	}

	private string GetEventPropertiesBody()
	{
		return "<GetEventProperties xmlns=\"http://www.onvif.org/ver10/events/wsdl\"/>";
	}

	private string CreatePullPointSubscriptionBody()
	{
		return "<CreatePullPointSubscription xmlns=\"http://www.onvif.org/ver10/events/wsdl\"><InitialTerminationTime>PT1H</InitialTerminationTime></CreatePullPointSubscription>";
	}

	private string RenewSubscriptionBody()
	{
		return "<Renew xmlns=\"http://docs.oasis-open.org/wsn/bw-2\"><TerminationTime>PT1H</TerminationTime></Renew>";
	}

	private string PullMessagesBody()
	{
		return "<PullMessages xmlns=\"http://www.onvif.org/ver10/events/wsdl\"><Timeout>PT5S</Timeout><MessageLimit>100</MessageLimit></PullMessages>";
	}

	private string UnsubscribeBody()
	{
		return "<Unsubscribe xmlns=\"http://docs.oasis-open.org/wsn/bw-2\"/>";
	}
}

}
