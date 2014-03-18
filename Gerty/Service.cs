namespace Gerty {

	using System;
	using System.Threading.Tasks;
	using System.Threading;
	using System.Diagnostics;
	using System.Collections.Generic;
	using System.Security.Cryptography;
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
		protected Stopwatch batchTimer;

		protected bool working;

		public Service(
			RedisConnection connection,
			Dispatcher dispatcher,
			Configuration configuration
		) {

			this.connection = connection;
			this.dispatcher = dispatcher;
			this.configuration = configuration;

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

		public async Task Work() {
		
			PreWorkAccounting();

			if(working)
				throw new Exception("Already working.");

			List<Task> jobs = new List<Task>();
			Log.Debug("Working.");
			working = true;
			while(working)
				jobs.Add(DoQueueJob());

			Log.Info("Waiting for all work to finish.");
			await Task.WhenAll(jobs);

			PostWorkAccounting();
			EnterListeningState();

		}
						
		//
		//
		//

		/**
		 * Communicates with Redis to obtain job data and decide what to do with it.
		 */
		protected async Task DoQueueJob() {

			// Reserve a job.
			byte[] rawJobData = await connection.Lists.RemoveLastAndAddFirst(0, configuration.Queue, configuration.Reserved);

			// If the queue is empty...
			if(rawJobData == null) {
			
				Log.Trace("No job found.");

				// If we're the first one to reach the end of the queue.
				if(working)
					working = false;

			}
			else {
				// We have a valid job.
				DoJob(rawJobData);
				// Unreserve one copy of the job.
				connection.Lists.Remove(0, configuration.Reserved, rawJobData, 1);
			}

		}

		/**
		 * Handle job data, outside of the claim lifecycle.
		 */
		protected async Task DoJob(byte[] rawJobData) {

			MD5 md5 = new MD5CryptoServiceProvider();

			string jobKey = BitConverter.ToString(md5.ComputeHash(rawJobData));
			string decodedJobData = Decode(rawJobData);

			// See if the job is a duplicate, if so, count it in the duplicate queue and skip.
			if(!await connection.Sets.Add(0, configuration.Recent, jobKey)) {
				connection.Hashes.Increment(0, configuration.Duplicate, decodedJobData);
			}
			// If we make it here, the job is safe to run.
			else {

				Tuple<bool, string> result = null;

				// Any errors thrown by the dispatcher belong in the job error log.
				try {
					result = await dispatcher.DispatchJob(decodedJobData);
				}
				catch(Exception ex) {
					Log.TraceException("Handler Error", ex);
					connection.Hashes.Set(0, configuration.Error, decodedJobData, ex.Message);
				}

				// If a handler was successfully run.
				if(result != null) {

					bool handled = result.Item1;
					byte[] rawHandledJobData = Encode(result.Item2);

					// If no handler claimed the work.
					if(!handled) {
						connection.Lists.AddFirst(0, configuration.Unclaimed, rawHandledJobData);
					}
					// If a handler claimed the work and we're logging completed work.
					else if(configuration.MaxCompleted > 0) {
						connection.Lists.AddFirst(0, configuration.Completed, rawHandledJobData);
						connection.Lists.Trim(0, configuration.Completed, 0, (configuration.MaxCompleted - 1));
					}

				}

			}

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
			Work();
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
		// Lifecycle Accounting
		//

		/**
		 * Called just before a work loop is begun.
		 */
		protected void PreWorkAccounting() {

			batchTimer.Restart();

			connection.Hashes.Remove(0, configuration.WorkerLog, "last_batch_total");
			connection.Hashes.Remove(0, configuration.WorkerLog, "last_batch_duration_ms");

			connection.Hashes.Set(0, configuration.WorkerLog, new Dictionary<string, byte[]>() {
				{"last_batch", Encode(DateTime.Now.ToString())}
			});

		}

		/**
		 * Called after all jobs from a work loop are completed.
		 */
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

		/**
		 * Called after all job logic is finished.
		 */
		protected void PostJobAccounting() {
			connection.Hashes.Increment(0, configuration.WorkerLog, "worker_total");
			connection.Hashes.Increment(0, configuration.WorkerLog, "last_batch_total");
		}

		//
		// Utilities
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

		//
		//
		//

		private class Operations {

			public Operations() {

			}

		}

	}

}