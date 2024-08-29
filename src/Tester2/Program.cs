using NLog;
using NLog.Loki;
namespace Tester2
{
	internal class Program
	{
		public static async Task TestMessages()
		{
			var logger = LogManager.GetLogger("Main");
            await Task.Delay(1000);
            logger.Debug($">>>>>>>>>>>>>>>>> PUSH START >>>>>>>>>>>>>>>>>");
            int mid = 0;
			for (int i = 0; i < 2; i++)
			{
				logger.Debug($"Debug - {mid++}");
				logger.Info( $"Info  - {mid++}");
				logger.Warn( $"Warn  - {mid++}");
				logger.Error($"Error - {mid++}");
				logger.Fatal($"Fatal - {mid++}");
				await Task.Delay(1_000);
			}
		}
		public static async Task Main(string[] args)
		{
		    // NLog
            var config = new NLog.Config.LoggingConfiguration();

            var lokiTarget = NLog.Loki.LokiTarget.GetLokiTarget(url: "http://devbuild-oel.kbclab.ro:3100", service_name: "test2");
            config.AddTarget(lokiTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, lokiTarget);

            NLog.LogManager.Configuration = config;

            await TestMessages();
            await Task.Delay(Timeout.Infinite);
        }
	}
}
