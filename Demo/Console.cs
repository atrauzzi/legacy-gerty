namespace Demo {

	using System;
	using System.Threading;
	using System.Threading.Tasks;
	using BookSleeve;
	using Gerty;
	using NLog;
	using NLog.Config;
	using NLog.Targets;


	public class Console {

		protected static Logger Log = LogManager.GetLogger(typeof(Console).AssemblyQualifiedName);

		public static void Main() {
		
			ConfigureLogging();

			Log.Info("Good morning Sam.");

			RedisConnection connection = new RedisConnection("localhost");
			Dispatcher dispatcher = new Dispatcher();
			Configuration configuration = new Configuration();

			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			CancellationToken cancellationToken = cancellationTokenSource.Token;

			// Just a test handler.
			dispatcher.addHandler(async (job) => {
				return true;
			});
				
			using(Service service = new Service(connection, dispatcher, configuration)) {
				service.Boot();
				service.Work(cancellationToken);
				// We have to block the program from terminating, just not with practical code.
				Thread.Sleep(Timeout.Infinite);
			}

			// Never executes ;)
			Log.Info("I hope life on Earth is everything you remember it to be."); 

		}

		protected static void ConfigureLogging() {

			LoggingConfiguration config = new LoggingConfiguration();

			ColoredConsoleTarget consoleTarget = new ColoredConsoleTarget();
			config.AddTarget("console", consoleTarget);
			consoleTarget.Layout = @"${machinename}:${date:format=HH\\:MM\\:ss} ${message}";

			LoggingRule debugRule = new LoggingRule("*", LogLevel.Debug, consoleTarget);
			config.LoggingRules.Add(debugRule);

			LogManager.Configuration = config;

		}

	}

}