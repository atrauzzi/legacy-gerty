namespace Gerty {

	using System;
	using System.Collections.Generic;
	using System.Threading.Tasks;
	using Newtonsoft.Json;


	public class Dispatcher {

		protected Queue<Func<Job, Task<bool>>> handlers;

		public Dispatcher() {
			handlers = new Queue<Func<Job, Task<bool>>>();
		}

		public void addHandler(Func<Job, Task<bool>> handler) {
			handlers.Enqueue(handler);
		}

		//
		//
		//

		public async Task<Tuple<bool, string>> DispatchJob(string rawJobData) {

			Job job = JsonConvert.DeserializeObject<Job>(rawJobData);
			bool handled = await DispatchJob(job);
			rawJobData = JsonConvert.SerializeObject(job);

			return new Tuple<bool, string>(handled, rawJobData);

		}

		public async Task<bool> DispatchJob(Job job) {

			bool handled = false;

			job.Attempts.Push(DateTime.Now);

			// Tell each listener about the job, first to claim wins!
			foreach(Func<Job, Task<bool>> listener in handlers) {
				handled = await listener(job);
				if(handled)
					break;
			}

			if(handled || handlers.Count == 0) {
				job.Completed = DateTime.Now;
			}

			return handled;

		}

	}

}