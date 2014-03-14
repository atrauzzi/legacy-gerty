namespace Demo {

	using System;
	using System.Threading;
	using BookSleeve;
	using Gerty;

	public class Console {

		public static void Main() {

			System.Console.Out.WriteLine("Good morning Sam.");

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


			System.Console.Out.WriteLineAsync("I hope life on Earth is everything you remember it to be.");

		}

	}

}