namespace Gerty {

	using System;
	using System.Threading.Tasks;
	using System.Threading;
	using System.Diagnostics;
	using System.Collections.Generic;
	using BookSleeve;


	public class Service : IDisposable {

		protected RedisConnection connection;
		protected Dispatcher dispatcher;
		protected Configuration configuration;

		protected RedisSubscriberConnection subscriberConnection;
		protected CancellationTokenSource internalCancellationSource;
		protected List<CancellationToken> cancellationTokens;
		protected Stopwatch batchTimer;

		public Service(
			RedisConnection connection,
			Dispatcher dispatcher,
			Configuration configuration
		) {

			this.connection = connection;
			this.dispatcher = dispatcher;
			this.configuration = configuration;

			cancellationTokens = new List<CancellationToken>();
			batchTimer = new Stopwatch();

		}

		public void Boot() {
			ConnectToRedis();
			batchTimer.Start();
		}

		protected void ConnectToRedis() {
			Console.Out.WriteLine("Connecting to redis.");
			connection.Open().Wait();
			Console.Out.WriteLine("Redis connection established.");
			///
			Console.Out.WriteLine("Opening subscription connection.");
			subscriberConnection = connection.GetOpenSubscriberChannel();
			Console.Out.WriteLine("Subscription connection established.");
		}

		//
		// Work Handling
		//

		/**
		 * Represents the process by which work is fetched.
		 * 
		 *  - Poll the queue asynchronously like a baeast!
		 *  - When no work is available, stop polling and become event-driven.
		 */
		public async Task Work(CancellationToken? externalCancellationToken) {

			if(externalCancellationToken.HasValue)
				cancellationTokens.Add(externalCancellationToken.Value);

			if(cancellationTokens.Count < 1)
				internalCancellationSource = new CancellationTokenSource();
			else
				internalCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokens.ToArray());

			PreWorkAccounting();
			Console.Out.WriteLine("Pulling from queue.");

			List<Task> jobRuns = new List<Task>();
			do {
				// Add each continuation to a collection which we can then wait on.
				jobRuns.Add(PullJob());
			} while(!internalCancellationSource.IsCancellationRequested);

			await Console.Out.WriteLineAsync("Waiting for all jobs to complete.");
			try {
				await Task.WhenAll(jobRuns.ToArray());
			}
			catch(Exception ex) {
				Console.Out.WriteLine(ex.ToString());
			}

			await Console.Out.WriteLineAsync("All jobs completed.");
			PostWorkAccounting();
			EnterListeningState();

		}

		/**
		 * Communicates with Redis to obtain job data and decide what to do with it.
		 */
		protected async Task PullJob() {

			// Claim a job.
			byte[] rawJobData = await connection.Lists.RemoveLastAndAddFirst(0, configuration.Queue, configuration.Reserved);

			// If we've found the end of the queue and a cancellation hasn't been requested yet...
			if(rawJobData == null) {

				if(!internalCancellationSource.IsCancellationRequested) {
					// For some reason, using the async variant sometimes causes "System.InvalidOperationException: Operation is not valid due to the current state of the object"
					// exceptions.  My suspicion is that it's during the end of a queue.
					//await Console.Out.WriteLineAsync("Queue is empty.");
					Console.Out.WriteLine("Queue is empty.");
					internalCancellationSource.Cancel();
				}

				// There is no job, don't try to log anything.
				return;

			}
			// See if the job is a duplicate, if so, send it to the duplicate queue and skip.
			if(!await connection.Sets.Add(0, configuration.Recent, rawJobData)) {
				connection.Hashes.Increment(0, configuration.Duplicate, Decode(rawJobData));
			}
			// If we make it here, a job was found and we're good to run it!
			else {
				try {

					Task<Tuple<bool, string>> dispatch = dispatcher.DispatchJob(Decode(rawJobData));
					if(dispatch.Exception != null)
						throw dispatch.Exception.InnerExceptions[0];

					Tuple<bool, string> result = await dispatch;
					bool handled = result.Item1;
					byte[] rawHandledJobData = Encode(result.Item2);

					// If a listener claimed the work and we're logging completed work...
					if(handled && configuration.MaxCompleted > 0) {
						connection.Lists.AddFirst(0, configuration.Completed, rawHandledJobData);
						connection.Lists.Trim(0, configuration.Completed, 0, (configuration.MaxCompleted - 1));
					}
					// If no listeners claimed the work...
					else {
						connection.Lists.AddFirst(0, configuration.Unclaimed, rawHandledJobData);
					}

				}
				catch(Exception ex) {
					connection.Hashes.Set(0, configuration.Error, Decode(rawJobData), ex.Message);
				}

			}

			// We're done with the job data, remove it from our reserved queue.
			connection.Lists.Remove(0, configuration.Reserved, rawJobData, 0);
			PostJobAccounting();

		}

		//
		// Listening Algorithm
		//

		/**
		 * Register a callback to allow the Redis server to signal when new work is available.
		 */
		protected void EnterListeningState() {
			Console.Out.WriteLine("Entering listening state.");
			subscriberConnection.Subscribe(configuration.Queue, WorkReady);
		}

		/**
		 * Upon receiving any indication of new work, we can start working off the queue.
		 */
		protected void WorkReady(string key, byte[] data) {
			Console.Out.WriteLine("");
			Console.Out.WriteLine(DateTime.Now.ToString());
			Console.Out.WriteLine("Work is available.");
			ExitListeningState();
			Work(null);
		}

		/**
		 * Called when we want to switch notification of new work.
		 */
		protected void ExitListeningState() {
			Console.Out.WriteLine("Exiting listening state.");
			subscriberConnection.Unsubscribe(configuration.Queue);
		}

		//
		// Redis-Specific Accounting
		//

		protected void PreWorkAccounting() {

			batchTimer.Restart();

			connection.Hashes.Remove(0, configuration.WorkerLog, "last_batch_total");
			connection.Hashes.Remove(0, configuration.WorkerLog, "last_batch_duration_ms");

			connection.Hashes.Set(0, configuration.WorkerLog, new Dictionary<string, byte[]>() {
				{"last_batch", Encode(DateTime.Now.ToString())}
			});

		}

		protected void PostWorkAccounting() {

			Console.Out.WriteLine(String.Concat("Elapsed time: ", batchTimer.ElapsedMilliseconds.ToString(), "ms"));

			connection.Hashes.Set(0, configuration.WorkerLog, new Dictionary<string, byte[]>() {
				// We have to do this at the end of each job as the last in the batch is the only one that knows the proper end time.
				{ "last_batch_duration_ms", Encode(batchTimer.ElapsedMilliseconds.ToString()) }
			});

			// If configured, update the rolling window for duplicate detection.
			if(configuration.DuplicateWindow > 0)
				connection.Keys.Expire(0, configuration.Recent, configuration.DuplicateWindow);

		}

		protected void PostJobAccounting() {
			connection.Hashes.Increment(0, configuration.WorkerLog, "worker_total");
			connection.Hashes.Increment(0, configuration.WorkerLog, "last_batch_total");
		}

		//
		//
		//

		/**
		 * If for any reason string encoding needs to be changed, it can be done here.
		 */
		protected byte[] Encode(string data) {
			return System.Text.Encoding.UTF8.GetBytes(data);
		}
		protected string Decode(byte[] data) {
			return System.Text.Encoding.UTF8.GetString(data);
		}

		public void Dispose() {
			Console.Out.WriteLine("Cleaning up.");
			// Remove this service's logs.
			connection.Keys.Remove(0, configuration.WorkerLog).Wait();
			// Consider: Put all the worker's pending jobs somewhere?
		}

	}

}