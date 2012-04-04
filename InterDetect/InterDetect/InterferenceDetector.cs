﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using NUnit.Framework;
using System.IO;
using Mage;
using System.Data.SQLite;
using ThermoRawFileReaderDLL;


namespace InterDetect
{



	public class InterferenceDetector
	{

        private const string tempfile = "prec_info_temp.txt";
        private const string raw_ext = ".raw";
        private const string isos_ext = "_isos.csv";

        [Test]
        public void DatabaseCheck()
        {
            DatabaseStuff(@"C:\DMS_WorkDir\Step_1_ASCORE");

        }

        /// <summary>
        /// Given a datapath makes queries to the database for isos file and raw file paths.  Uses these 
        /// to generate an interference table and adds this table to the database
        /// </summary>
        /// <param name="datapath">directory to the database, assumed that database is called Results.db3</param>
        private void DatabaseStuff(string datapath)
        {
            // build Mage pipeline to read contents of 
            // a table in a SQLite database into a data buffer

            // first, make the Mage SQLite reader module
            // and configure it to read the table
            SQLiteReader reader = new SQLiteReader();
            reader.Database = Path.Combine(datapath,"Results.db3");


            reader.SQLText = "SELECT * FROM t_msms_raw_files;";

            // next, make a Mage sink module (simple row buffer)
            SimpleSink sink = new SimpleSink();

            // construct and run the Mage pipeline
            ProcessingPipeline.Assemble("Test_Pipeline", reader, sink).RunRoot(null);


            // example of reading the rows in the buffer object
            // (dump folder column values to Console)
            int folderPathIdx = sink.ColumnIndex["Folder"];
            int datasetIDIdx = sink.ColumnIndex["Dataset_ID"];
            int datasetIdx = sink.ColumnIndex["Dataset"];
            Dictionary<string, string> filepaths = new Dictionary<string, string>();
            foreach (object[] row in sink.Rows)
            {
                filepaths.Add(row[datasetIDIdx].ToString(), Path.Combine(row[folderPathIdx].ToString(), row[datasetIdx].ToString() + raw_ext));


            }
            //Restart the buffer
            sink = new SimpleSink();

            //Add rows from other table
            reader.SQLText = "SELECT * FROM t_decon2ls_job";

            // construct and run the Mage pipeline
            ProcessingPipeline.Assemble("Test_Pipeline2", reader, sink).RunRoot(null);
           

            int folder = sink.ColumnIndex["Folder"];
            int datasetID = sink.ColumnIndex["Dataset_ID"];
            int dataset = sink.ColumnIndex["Dataset"];
            Dictionary<string, string> isosPaths = new Dictionary<string, string>();
            //store the filepaths indexed by datasetID
            foreach (object[] row in sink.Rows)
            {
                isosPaths.Add(row[datasetID].ToString(),Path.Combine(row[folder].ToString(), row[dataset].ToString() + isos_ext));
            }


            //Calculate the needed info and generate a temporary file, keep adding each dataset to this file
            foreach (string files in filepaths.Keys)
            {
                List<PrecursorInfo> myInfo = ParentInfoPass(filepaths[files], isosPaths[files]);
                PrintInterference(myInfo, files, Path.Combine(datapath, "prec_info_temp.txt"));
            }

            //Create a delimeted file reader and write a new table with this info to database
            DelimitedFileReader delimreader = new DelimitedFileReader();
            delimreader.FilePath = Path.Combine(datapath, tempfile);

            SQLiteWriter writer = new SQLiteWriter();
            string tableName = "t_precursor_interference";
            writer.DbPath = Path.Combine(datapath, "Results.db3");
            writer.TableName = tableName;

            ProcessingPipeline.Assemble("ImportToSQLite", delimreader, writer).RunRoot(null);


            //cleanup
            File.Delete(Path.Combine(datapath, tempfile));


        }

        [Test]
        public void TestParentInfoPass1()
        {
        //    GetParentInfo parentInfo = new GetParentInfoSequest(@"\\proto-9\VOrbiETD03\2011_4\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28\Seq201111091803_Auto765070\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28_fht.txt");
            List<PrecursorInfo> myInfo = ParentInfoPass(@"\\proto-9\VOrbiETD03\2012_1\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09.raw",
                @"\\proto-9\VOrbiETD03\2012_1\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09\DLS201201111344_Auto783501\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09_isos.csv");
            
   //         PrintInterference(myInfo, @"C:\Users\aldr699\Documents\2012\InterferenceTesting\here2.txt");


        }

        [Test]
        public void TestInterference()
        {
       //     GetParentInfo parentInfo = new GetParentInfoSequest(@"\\proto-9\VOrbiETD03\2012_1\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09\DLS201201111344_Auto783501\Isobaric_iTRAQ8_5ug_Run1_10Jan12_Cougar_11-10-09_isos");
            //List<PrecursorInfo> myInfo = ParentInfoPass(@"\\proto-9\VOrbiETD03\2011_4\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28.raw", parentInfo);
            //Interference(ref myInfo, @"\\proto-9\VOrbiETD03\2011_4\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28\E_ligno_SCF1_LX_pool_01_01Oct11_Lynx_11-09-28.raw"); 
            
            //PrintInterference(myInfo, @"C:\Users\aldr699\Documents\2012\InterferenceTesting\here2.txt");
        }

        /// <summary>
        /// Collects the parent ion information as well as inteference 
        /// </summary>
        /// <param name="rawfile"></param>
        /// <param name="isosfile"></param>
        /// <returns>Provides a precursor info list</returns>
		public static List<PrecursorInfo> ParentInfoPass(string rawfile, string isosfile)
		{
            ThermoRawFileReaderDLL.FinniganFileIO.XRawFileIO myRaw = new ThermoRawFileReaderDLL.FinniganFileIO.XRawFileIO();
            myRaw.OpenRawFile(rawfile);
            IsosHandler isos = new IsosHandler(isosfile);
            //open raw file and set controller

            List<PrecursorInfo> preInfo = new List<PrecursorInfo>();
            int numSpectra = myRaw.GetNumScans();
			//TODO: Add error code for 0 spectra
			int currPrecScan = 0;
            //Go into each scan and collect precursor info.
			for (int i = 1; i <= numSpectra; i++)
			{
				int msorder = 2;
                if (isos.IsParentScan(i))
                    msorder = 1;

 
                ThermoRawFileReaderDLL.FinniganFileIO.FinniganFileReaderBaseClass.udtScanHeaderInfoType ude = 
                    new ThermoRawFileReaderDLL.FinniganFileIO.FinniganFileReaderBaseClass.udtScanHeaderInfoType();
                myRaw.GetScanInfo(i, ref ude);

				if(msorder > 1)
				{
                    int chargeState = Convert.ToInt32(ude.ScanEventValues[11]);
                    double mz = Convert.ToDouble(ude.ScanEventValues[12]);
                    double isolationWidth = Convert.ToDouble(ude.ScanEventValues[13]);
                    if (chargeState == 0)
                    {
                        if (!isos.GetChargeState(i, mz, ref chargeState))
                        {
                            continue;
                        }
                    }

                    PrecursorInfo info = new PrecursorInfo();
                    info.dIsoloationMass = mz;
                    info.nScanNumber = i;
                    info.preScanNumber = currPrecScan;
                    info.nChargeState = chargeState;
                    info.isolationwidth = isolationWidth;
                    Interference(ref info, isos);
                    preInfo.Add(info);
                    
                    
				}
				else if(msorder == 1)
				{
					currPrecScan = i;
				}
				
			}
            myRaw.CloseRawFile();
            return preInfo;
		}

        /// <summary>
        /// Print our table to a temporary file
        /// </summary>
        /// <param name="preinfo"></param>
        /// <param name="datasetID">Id number is a string because thats what sql gives me and there
        /// is no point in switching it back and forth</param>
        /// <param name="filepath"></param>
        private void PrintInterference(List<PrecursorInfo> preinfo, string datasetID, string filepath)
        {
            bool fieldExistance = File.Exists(filepath);
            using (StreamWriter sw = new StreamWriter(filepath,fieldExistance))
            {
                if (!fieldExistance)
                {
                    sw.Write("Dataset_ID\tScanNumber\tPrecursorScan\tParentMZ\tChargeState\tIsoWidth\tInterference\n");
                }

                foreach(PrecursorInfo info in preinfo)
                {
                    sw.Write(datasetID + "\t" + info.nScanNumber + "\t" + info.preScanNumber + "\t" +
                        info.dIsoloationMass + "\t" + info.nChargeState + "\t" + 
                        info.isolationwidth + "\t" + info.interference + "\n");
                }
            }
        }

        /// <summary>
        /// Finds the interference of the target isotopic profile
        /// </summary>
        /// <param name="preInfo"></param>
        /// <param name="isos"></param>
        private static void Interference(ref PrecursorInfo preInfo, IsosHandler isos)
        {

            const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
            double PreErrorAllowed = 10.0;
            double low = preInfo.dIsoloationMass - (preInfo.isolationwidth/2.0);
            double high = preInfo.dIsoloationMass + (preInfo.isolationwidth/2.0);
            
            double[,] peaks = isos.GetPeakList2(preInfo.preScanNumber, low, high);

            double MaxPreInt = 0;
            double MaxInterfereInt = 0;
          //  int p = peaks.GetUpperBound(1);
            double OverallInterference = 0;
            if (peaks != null)
            {
                for (int j = peaks.GetLowerBound(1); j <= peaks.GetUpperBound(1); j++)
                {
                    double difference = (peaks[0, j] - preInfo.dIsoloationMass) * preInfo.nChargeState;
                    double difference_Rounded = Math.Round(difference);
                    double expected_difference = difference_Rounded * C12_C13_MASS_DIFFERENCE;
                    double Difference_ppm = Math.Abs((expected_difference - difference) /
                        (preInfo.dIsoloationMass * preInfo.nChargeState)) * 1000000;

                    if (Difference_ppm < PreErrorAllowed)
                    {
                        MaxPreInt += peaks[1, j];
                    }

                    MaxInterfereInt += peaks[1, j];

                }
                OverallInterference = MaxPreInt / MaxInterfereInt;
            }
            preInfo.interference = OverallInterference;
            //DatasetPrep.Utilities.WriteDataTableToText(dt, fhtFile.Substring(0, fhtFile.Length - 4) + "_int.txt");
        }


	}
}
