using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DeconTools.Backend;
using DeconTools.Backend.Core;
using DeconTools.Backend.Utilities;
using DeconTools.Backend.ProcessingTasks;
using System.Data;
using NUnit.Framework;
using XRAWFILE2Lib;

namespace PeakDetect
{
	public class Interference
	{

		[Test]
		public void Test1()
		{
			DataTable dt = TextFileToData(@"C:\Documents and Settings\aldr699\My Documents\Visual Studio 2010\Projects\Jan2012\AdjustedCoverageSummarizer\InterDetect\InterDetect\TestFile\result.txt");
			Interference2(ref dt, @"\\proto-9\VOrbiETD04\2011_3\DTRA_iTRAQ_0_10Gy_CH2_2D_03_21_2011_06\DTRA_iTRAQ_0_10Gy_CH2_2D_03_21_2011_06.raw");
			WriteDataTableToText(dt, @"C:\Documents and Settings\aldr699\My Documents\Visual Studio 2010\Projects\Jan2012\AdjustedCoverageSummarizer\InterDetect\InterDetect\TestFile\result2.txt");

		}

		public static DataTable Interference2(ref DataTable dt, string rawFile)
		{

			const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
			double PreErrorAllowed = 10.0;

			//	DatasetPrep.PrepareDataset.GetParentInfo(fhtFile, rawFile);
			//		dt.Columns.Add("Interference", typeof(double));

			DeconTools.Backend.Runs.RunFactory runFactory = new DeconTools.Backend.Runs.RunFactory();
			Run run = runFactory.CreateRun(rawFile);

			Task msgen = MSGeneratorFactory.CreateMSGenerator(run.MSFileType);

			var peakdetector = new DeconToolsPeakDetector();

			for (int i = 0; i < dt.Rows.Count; i++)
			{
				Console.Write("\rProgress" + (i + 1) + @" / " + dt.Rows.Count);
				int charge = (int)dt.Rows[i]["ChargeState"];
				int precursorNum = (int)dt.Rows[i]["ParentScanNum"];
				double preMZ = (double)dt.Rows[i]["PrecursorMass"];
				double isoWidth = (double)dt.Rows[i]["IsolationWidth"];
				ScanSet scanset = new ScanSet(precursorNum);
				run.CurrentScanSet = scanset;
				msgen.Execute(run.ResultCollection);
				peakdetector.Execute(run.ResultCollection);
				List<IPeak> myPeaks = run.PeakList;

				List<IPeak> isoPeaks = myPeaks.FindAll(delegate(IPeak peak)
				{
					return peak.XValue < preMZ + 0.5 * isoWidth && peak.XValue > preMZ - 0.5 * isoWidth;
				});
				double MaxPreInt = 0;
				double MaxInterfereInt = 0;
				for (int j = 0; j < isoPeaks.Count; j++)
				{
					double difference = (isoPeaks[j].XValue - preMZ) * charge;
					double difference_Rounded = Math.Round(difference);
					double expected_difference = difference_Rounded * C12_C13_MASS_DIFFERENCE;
					double Difference_ppm = (Math.Abs((expected_difference - difference) / (preMZ * charge))) * 1000000;

					if (Difference_ppm < PreErrorAllowed)
					{
						MaxPreInt += isoPeaks[j].Height;
					}

					MaxInterfereInt += isoPeaks[j].Height;

				}
				double OverallInterference = MaxPreInt / MaxInterfereInt;
				dt.Rows[i]["Interference"] = OverallInterference;
			}

			//DatasetPrep.Utilities.WriteDataTableToText(dt, fhtFile.Substring(0, fhtFile.Length - 4) + "_int.txt");
			return dt;
		}



		public static DataTable TextFileToData(string fileName)
		{
			string line = "";
			string[] fields = null;
			DataTable dt = new DataTable();


			try
			{
				using (System.IO.StreamReader sr = new System.IO.StreamReader(fileName))
				{
					// first line has headers   
					List<int> myFields = new List<int>();
					if ((line = sr.ReadLine()) != null)
					{
						fields = line.Split(new char[] { '\t', ',' });

						int mycount = 0;
						foreach (string s in fields)
						{
							if (s == "ScanNum" || s == "Job" || s == "DatasetNum" || s == "MultiProtein"
								|| s == "ID" || s == "ScanNumber" || s == "Sample" || s == "HCDScanNum"
								|| s == "ChargeState" || s == "ParentScanNum")
							{
								dt.Columns.Add(s, typeof(int));
								dt.Columns[s].DefaultValue = 0;
								myFields.Add(mycount);

							}
							else if (s == "ReporterIonIntensityMax" || s == "MSGF_SpecProb" || s == "SpecProb" ||
								s == "Ion_113" || s == "Ion_114" || s == "Ion_115" || s == "Ion_116" ||
								s == "Ion_117" || s == "Ion_118" || s == "Ion_119" || s == "Ion_121" ||
								s == "PrecursorMass" || s == "IsolationWidth")
							{
								dt.Columns.Add(s, typeof(double));
								dt.Columns[s].DefaultValue = 0.0;
								myFields.Add(mycount);

							}
							else if (s == "Peptide" || s == "Reference" || s == "Dataset" || s == "Collision Mode")
							{
								dt.Columns.Add(s);
								dt.Columns[s].DefaultValue = "";
								myFields.Add(mycount);

							}
							else
							{
								dt.Columns.Add(s, typeof(string));
								dt.Columns[s].DefaultValue = "";
								myFields.Add(mycount);
							}
							mycount++;
						}

					}
					else
					{
						// it's empty, that's an error   
						throw new ApplicationException("The data provided is not in a valid format.");
					}
					// fill the rest of the table; positional   
					while ((line = sr.ReadLine()) != null)
					{
						DataRow row = dt.NewRow();

						fields = line.Split(new char[] { '\t', ',' });
						if (fields.Length < myFields.Count)
						{
							continue;
						}
						int i = 0;
						foreach (int s in myFields)
						{
							if ((dt.Columns[i].ColumnName == "MSGF_SpecProb" || dt.Columns[i].ColumnName == "SpecProb")
								&& fields[s] == "")
							{
								row[i] = 1;
							}
							else
							{
								row[i] = fields[s];
							}
							i++;
						}


						dt.Rows.Add(row);
					}
				}
				//if (!dt.Columns.Contains("DatasetName")&& s)
				//{
				//    string dataTitle = System.IO.Path.GetFileName(filename).Split('.')[0];

				//    dt.Columns.Add("DataSetName", typeof(string));
				//    foreach (DataRow row in dt.Rows)
				//    {
				//        row["DataSetName"] = dataTitle;
				//    }
				//}

				return dt;
			}
			catch (Exception ex)
			{
				Console.WriteLine("The file failed to load" + ex.Message);
				return null;
			}
		}



		/// <summary>
		/// Writes a datatable to text file
		/// </summary>
		/// <param name="dt"></param>
		/// <param name="filePath"></param>
		public static void WriteDataTableToText(DataTable dt, string filePath)
		{
			using (System.IO.StreamWriter sw = new System.IO.StreamWriter(filePath))
			{
				string s = dt.Columns[0].ColumnName;
				for (int i = 1; i < dt.Columns.Count; i++)
				{
					s += "\t" + dt.Columns[i].ColumnName;
				}
				sw.WriteLine(s);

				s = string.Empty;
				foreach (DataRow row in dt.Rows)
				{
					s = "" + row[0];
					for (int i = 1; i < dt.Columns.Count; i++)
					{
						s += "\t" + row[i];
					}
					sw.WriteLine(s);
					s = string.Empty;
				}


			}
		}



	}
}
