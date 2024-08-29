using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Loki.Model;
using NLog.Targets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace NLog.Loki
{
    [Target("loki")]
    public class LokiTarget : AsyncTaskTarget
    {
        private readonly Lazy<ILokiTransport> lazyLokiTransport;

        [RequiredParameter]
        public Layout Endpoint { get; set; }

        [ArrayParameter(typeof(LokiTargetLabel), "label")]
        public IList<LokiTargetLabel> Labels { get; }

        public static Func<Uri, ILokiHttpClient> LokiHttpClientFactory { get; set; } = GetLokiHttpClient;

        public LokiTarget()
        {
            Labels = new List<LokiTargetLabel>();

            lazyLokiTransport =
                new Lazy<ILokiTransport>(
                    () => GetLokiTransport(Endpoint),
                    LazyThreadSafetyMode.ExecutionAndPublication);
        }

        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            var @event = GetLokiEvent(logEvent);

            return lazyLokiTransport.Value.WriteLogEventsAsync(new[] { @event });
        }

        protected override Task WriteAsyncTask(IList<LogEventInfo> logEvents, CancellationToken cancellationToken)
        {
            var events = GetLokiEvents(logEvents);

            return lazyLokiTransport.Value.WriteLogEventsAsync(events);
        }

        private IEnumerable<LokiEvent> GetLokiEvents(IEnumerable<LogEventInfo> logEvents)
        {
            return logEvents.Select(GetLokiEvent);
        }

        private LokiEvent GetLokiEvent(LogEventInfo logEvent)
        {
            var labels =
                new LokiLabels(
                    Labels.Select(
                        ltl => new LokiLabel(ltl.Name, ltl.Layout.Render(logEvent))));

            var line = RenderLogEvent(Layout, logEvent);

            // the line object may have a time property, use that if available
            DateTime timestamp = logEvent.TimeStamp.ToLocalTime();
            try
            {
                JObject jo = JsonConvert.DeserializeObject<JObject>(line, new JsonSerializerSettings() { DateParseHandling = DateParseHandling.None });
                string key = null;
                if(jo.ContainsKey("time"))
                    key = "time";
                else if(jo.ContainsKey("timestamp"))
                    key = "timestamp";

                if(key != null)
                {
                    if(jo[key].Type == JTokenType.String) // Assume ISO 8601 (UTC)
                    {
                        string ts = jo[key].Value<string>() ?? throw new Exception("Could not extract timestamp");
                        timestamp = DateTime.Parse(ts).ToUniversalTime();
                    }
                }
            } catch(Exception) { }

            var @event = new LokiEvent(labels, timestamp, line);
            return @event;
        }

        internal ILokiTransport GetLokiTransport(Layout endpoint)
        {
            var endpointUri = RenderLogEvent(endpoint, LogEventInfo.CreateNullEvent());
            if(Uri.TryCreate(endpointUri, UriKind.Absolute, out var uri))
            {
                if(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                {
                    var lokiHttpClient = LokiHttpClientFactory(uri);
                    var httpLokiTransport = new HttpLokiTransport(uri, lokiHttpClient);

                    return httpLokiTransport;
                }
            }

            InternalLogger.Warn("Unable to create a valid Loki Endpoint URI from '{0}'", endpoint);

            var nullLokiTransport = new NullLokiTransport();

            return nullLokiTransport;
        }

        internal static ILokiHttpClient GetLokiHttpClient(Uri uri)
        {
            var httpClient = new HttpClient { BaseAddress = uri };
            var lokiHttpClient = new LokiHttpClient(httpClient);

            return lokiHttpClient;
        }


        /// <summary>
        /// Will increment all same timestamps with 1, so the logs maintain their order
        /// </summary>
        /// <param name="url"></param>
        /// <param name="service_name"></param>
        /// <param name="batch_size"
        /// <returns></returns>
        public static LokiTarget GetLokiTarget(string url, string service_name, int batch_size = 1)
        {
            #region Layouts
            long lastSeenTimestamp = 0;
            object lastSeenTimestampLock = new object();
            var timestampNoDuplicatesJL = NLog.Layouts.JsonLayout.FromMethod(logEvent =>
            {
                DateTime d = logEvent.TimeStamp.ToUniversalTime();
                long u1 = ((DateTimeOffset)d).ToUnixTimeMilliseconds();
                long u2 = 0;
                lock(lastSeenTimestampLock)
                {
                    u2 = u1 <= lastSeenTimestamp ? lastSeenTimestamp + 1 : u1;
                    lastSeenTimestamp = Math.Max(lastSeenTimestamp, u2);
                }
                d = d.AddMilliseconds(u2 - u1);
                return d.ToString("o");
            });
            var jsonLayout = new NLog.Layouts.JsonLayout
            {
                Attributes = {
                    new NLog.Layouts.JsonAttribute("time", timestampNoDuplicatesJL), // UTC Time 8601 Format
                    new NLog.Layouts.JsonAttribute("level", "${level}"),
                    new NLog.Layouts.JsonAttribute("logger", "${logger}"),
                    new NLog.Layouts.JsonAttribute("message", "${message}"),
                    new NLog.Layouts.JsonAttribute("properties", new NLog.Layouts.JsonLayout { IncludeEventProperties = true, MaxRecursionLimit = 2 }, encode: false),
                    new NLog.Layouts.JsonAttribute("exception", new NLog.Layouts.JsonLayout
                    {
                        Attributes =
                        {
                            new NLog.Layouts.JsonAttribute("type", "${exception:format=type}"),
                            new NLog.Layouts.JsonAttribute("message", "${exception:format=message}"),
                            new NLog.Layouts.JsonAttribute("stacktrace", "${exception:format=tostring}"),
                        }
                    }, encode: false), // don't escape layout
			    }
            };
            #endregion

            var lokiTarget = new LokiTarget
            {
                Name = "LokiTarget",
                Endpoint = url,
                Labels = {
                    new LokiTargetLabel {
                        Name = "machine",
                        Layout = NLog.Layouts.Layout.FromString("${machinename:lowercase=true}")
                    },
                    new LokiTargetLabel
                    {
                        Name = "user",
                        Layout = NLog.Layouts.Layout.FromString(Environment.UserName)
                    },
                    new LokiTargetLabel
                    {
                        Name = "service_name",
                        Layout = NLog.Layouts.Layout.FromString(service_name)
                    },
                    new LokiTargetLabel
                    {
                        Name = "level",
                        Layout = NLog.Layouts.Layout.FromString("${level:lowercase=true}")
                    }
                },
                Layout = jsonLayout,
                BatchSize = batch_size
            };

            return lokiTarget;
        }
    }
}
