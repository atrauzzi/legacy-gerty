namespace Gerty {

	using System;
	using System.Collections.Generic;


	public class Job {

		/**
		 * A string identifying the transport this message belongs to.
		 */
		public string Handler { get; set; }

		/**
		 * Stores the job-specific schema.
		 */
		public Dictionary<string, string> Data { get; set; }

		/**
		 * Each attempt to send is tracked here.
		 */
		public Stack<DateTime> Attempts { get; set; }

		/**
		 * Once sent, will contain when the successful send occurred.
		 */
		public DateTime Completed { get; set; }

		// Simple constructor ensures our collections are always initialized.
		public Job() {
			Data = new Dictionary<string, string>();
			Attempts = new Stack<DateTime>();
		}

	}

}