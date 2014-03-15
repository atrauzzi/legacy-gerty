namespace Gerty {

	using System;
	using System.Threading.Tasks;
	using System.Threading;
	using System.Diagnostics;
	using System.Collections.Generic;
	//
	using NLog;
	using BookSleeve;


	public class Service : IDisposable {

		protected static Logger Log = LogManager.GetLogger(typeof(Service).AssemblyQualifiedName);

		//
		//
		//

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
			Log.Info("Connecting to redis.");
			connection.Open().Wait();
			Log.Debug("Redis connection established.");
			///
			Log.Info("Opening subscription connection.");
			subscriberConnection = connection.GetOpenSubscriberChannel();
			Log.Debug("Subscription connection established.");
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
			Log.Debug("Pulling from queue.");

			List<Task> jobRuns = new List<Task>();
			do {
				// Add each continuation to a collection which we can then wait on.
				jobRuns.Add(PullJob());
			} while(!internalCancellationSource.IsCancellationRequested);

			Log.Info("Waiting for all jobs to complete.");
			try {
				await Task.WhenAll(jobRuns.ToArray());
			}
			catch(Exception ex) {
				Log.ErrorException("Job Failed", ex);
			}

			Log.Debug("All jobs completed.");
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
					Log.Debug("Queue is empty.");
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

					//Task<Tuple<bool, string>> dispatch = dispatcher.DispatchJob(Decode(rawJobData));
					//Tuple<bool, string> result = await dispatch;
					//if(dispatch.Exception != null)
					//	throw dispatch.Exception.InnerException;

					Tuple<bool, string> result = await dispatcher.DispatchJob(Decode(rawJobData));

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
			Log.Info("Entering listening state.");
			subscriberConnection.Subscribe(configuration.Queue, WorkReady);
		}

		/**
		 * Upon receiving any indication of new work, we can start working off the queue.
		 */
		protected void WorkReady(string key, byte[] data) {
			Log.Debug("Work is available.");
			ExitListeningState();
			Work(null);
		}

		/**
		 * Called when we want to switch notification of new work.
		 */
		protected void ExitListeningState() {
			Log.Info(DateTime.Now.ToString());
			Log.Info("Exiting listening state.");
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

			Log.Info(String.Concat("Elapsed time: ", batchTimer.ElapsedMilliseconds.ToString(), "ms"));

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
			Log.Debug("Cleaning up.");
			// Remove this service's logs.
			connection.Keys.Remove(0, configuration.WorkerLog).Wait();
			// Consider: Put all the worker's pending jobs somewhere?
		}

	}

}