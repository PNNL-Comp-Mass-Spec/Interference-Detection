using System;
using InterDetect;

namespace IDM_Console
{
	class Program
	{
		static void Main(string[] args)
		{
			var workDir = "..";
			var sourceFileName = "Results.db3";

		    var idm = new InterferenceDetector {ShowProgressAtConsole = false};

		    idm.ProgressChanged += InterfenceDetectorProgressHandler;

			var success = idm.Run(workDir, sourceFileName);

			if (success)
				Console.WriteLine("Success");
			else
				Console.WriteLine("Failed");
		}

		private static void InterfenceDetectorProgressHandler(InterferenceDetector id, ProgressInfo e)
		{
			Console.WriteLine(e.ProgressCurrentFile.ToString("0.0") + "% complete; " + e.Value.ToString("0.0") + "% complete overall");
		}
	}
}
