namespace Gerty {

	using System;
	using System.Diagnostics;


	public class Configuration {
	
		public string Identifier { get; set;}

		public string Namespace { get; set; }

		public int MaxCompleted { get; set; }

		public int DuplicateWindow { get; set; }

		//
		// Derived via convention
		//

		public string Queue { 
			get { 
				return string.Concat(Namespace, ":job:queue");
			}
		}

		public string Completed {
			get {
				return string.Concat(Namespace, ":job:completed");
			}
		}

		public string Unclaimed {
			get {
				return string.Concat(Namespace, ":job:unclaimed");
			}
		}

		public string Duplicate {
			get {
				return string.Concat(Namespace, ":job:duplicate");
			}
		}

		public string Error {
			get {
				return string.Concat(Namespace, ":job:error");
			}
		}

		public string Recent {
			get {
				return string.Concat(Namespace, ":job:recent");
			}
		}

		public string Reserved {
			get {
				return string.Concat(Namespace, ":job:reserved");
			}
		}

		public string WorkerLog {
			get {
				return string.Concat(Namespace, ":log:", Identifier);
			}
		}
			

		// Initializes a default configuration.
		public Configuration() {

			Identifier = Process.GetCurrentProcess().Id.ToString();
			Namespace = "gerty";
			MaxCompleted = 100;
			// 15 minutes
			DuplicateWindow = 900;

		}

	}

}