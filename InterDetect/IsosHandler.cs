﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using PRISM;

namespace InterDetect
{
    public class IsosHandler : EventNotifier
    {
        // Ignore Spelling: Isos, dt

        private Dictionary<int, List<IsosData>> mParentScans;

        /// <summary>
        /// When true, events are thrown up the calling tree for the parent class to handle them
        /// </summary>
        /// <remarks>Defaults to true</remarks>
        public bool ThrowEvents { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="throwEvents"></param>
        public IsosHandler(string filepath, bool throwEvents = true)
        {
            ThrowEvents = throwEvents;
            var isosData = TextFileToData(filepath);

            GetParentScanNumbers(isosData);
        }

        /// <summary>
        /// Find out if a parent scan
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public bool IsParentScan(int i)
        {
            return mParentScans.ContainsKey(i);
        }

        /// <summary>
        /// In some cases the raw file fails to provide a charge state, if that is the case
        /// we check the isos file to see if decon2ls could figure it out.
        /// </summary>
        /// <param name="scan"></param>
        /// <param name="mz"></param>
        /// <param name="charge"></param>
        /// <returns>True if a match was found, otherwise false</returns>
        public bool GetChargeState(int scan, double mz, ref int charge)
        {
            if (!IsParentScan(scan))
            {
                return false;
            }

            var isosMatch = (from parent in mParentScans[scan].AsEnumerable()
                             where parent.Mz > mz - 0.005 &&
                                   parent.Mz < mz + 0.005
                             select parent).ToList();

            if (isosMatch.Count > 0)
            {
                charge = isosMatch.First().Charge;
            }

            return charge != 0;
        }

        // Unused function
        //
        ///// <summary>
        ///// Get a peak list for this range and scan number.
        ///// </summary>
        ///// <param name="scan">scan number</param>
        ///// <param name="low">low mz value</param>
        ///// <param name="high">high mz value</param>
        ///// <returns></returns>
        //public double[,] GetPeakList(int scan, double low, double high)
        //{

        //    //change this to questioning a SortedDictionary<int, List<int>>();
        //    DataRow[] foundRows = mData.Select("[scan_num]='" + scan + "' AND [mz] > '" + low +
        //        "' AND [mz] < '" + high + "'");

        //    int count = foundRows.Length;

        //    if (count > 0)
        //    {
        //        var data = new double[2, count];
        //        for (int i = 0; i < count; i++)
        //        {
        //            data[0, i] = (double)foundRows[i]["mz"];
        //            data[1, i] = (double)foundRows[i]["abundance"];
        //        }
        //        return data;

        //    }
        //    return null;
        //}

        // Unused function
        //
        ///// <summary>
        ///// More efficient version of the GetPeakList.
        ///// Get a peak list for this range and scan number.
        ///// </summary>
        ///// <param name="scan">scan number</param>
        ///// <param name="low">low mz value</param>
        ///// <param name="high">high mz value</param>
        ///// <returns></returns>
        //public double[,] GetPeakList2(int scan, double low, double high)
        //{

        //    //change this to questioning a SortedDictionary<int, List<int>>();
        //    //DataRow[] foundRows = dt.Select("[scan_num]='" + scan + "' AND [mz] > '" + low +
        //    //    "' AND [mz] < '" + high + "'");

        //    DataRow[] foundRows = (from parent in parentScans[scan].AsEnumerable()
        //                                  where parent.Field<double>("mz") > low &&
        //                                  parent.Field<double>("mz") < high
        //                                  select parent).ToArray();

        //    int count = foundRows.Length;

        //    if (count > 0)
        //    {
        //        var data = new double[2, count];
        //        for (int i = 0; i < count; i++)
        //        {
        //            data[0, i] = (double)foundRows[i]["mz"];
        //            data[1, i] = (double)foundRows[i]["abundance"];
        //        }
        //        return data;

        //    }
        //    return null;
        //}

        /// <summary>
        /// Generates a list of parent scan numbers
        /// </summary>
        private void GetParentScanNumbers(IEnumerable<IsosData> isosData)
        {
            mParentScans = new Dictionary<int, List<IsosData>>();
            foreach (var isosEntry in isosData)
            {
                if (!mParentScans.ContainsKey(isosEntry.ScanNum))
                {
                    mParentScans.Add(isosEntry.ScanNum, new List<IsosData> { isosEntry });
                }
                else
                {
                    mParentScans[isosEntry.ScanNum].Add(isosEntry);
                }
            }
        }

        // Unused function
        //
        ///// <summary>
        ///// Writes a data table to text file
        ///// </summary>
        ///// <param name="dt"></param>
        ///// <param name="filePath"></param>
        //public static void WriteDataTableToText(DataTable dt, string filePath)
        //{
        //    using (var writer = new StreamWriter(filePath))
        //    {
        //        string s = dt.Columns[0].ColumnName;
        //        for (int i = 1; i < dt.Columns.Count; i++)
        //        {
        //            s += "\t" + dt.Columns[i].ColumnName;
        //        }
        //        writer.WriteLine(s);

        //        for each (DataRow currentRow in dt.Rows)
        //        {
        //            s = "" + currentRow[0];
        //            for (int i = 1; i < dt.Columns.Count; i++)
        //            {
        //                s += "\t" + currentRow[i];
        //            }
        //            writer.WriteLine(s);
        //        }
        //    }
        //}

        /// <summary>
        /// Converts a text file to a data table
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        protected List<IsosData> TextFileToData(string fileName)
        {
            try
            {
                var splitChars = new[] { '\t', ',' };

                var isosData = new List<IsosData>();

                using (var reader = new StreamReader(new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    if (reader.EndOfStream)
                    {
                        var message = "Data file is empty: " + fileName;
                        OnErrorEvent(message);
                        if (ThrowEvents)
                            throw new Exception(message);
                        return isosData;
                    }

                    var abundanceColIndex = -1;
                    var mzColIndex = -1;
                    var scanColIndex = -1;
                    var chargeColIndex = -1;

                    var headerLine = reader.ReadLine();

                    if (string.IsNullOrWhiteSpace(headerLine))
                    {
                        // it's empty, that's an error
                        var message = "Data file is empty: " + fileName;
                        OnErrorEvent(message);
                        if (ThrowEvents)
                            throw new Exception(message);
                        return isosData;
                    }

                    var headerCols = headerLine.Split(splitChars);

                    for (var i = 0; i < headerCols.Length; i++)
                    {
                        switch (headerCols[i].ToLower())
                        {
                            case "abundance":
                                abundanceColIndex = i;
                                break;
                            case "mz":
                                mzColIndex = i;
                                break;
                            case "scan_num":
                                scanColIndex = i;
                                break;
                            case "charge":
                                chargeColIndex = i;
                                break;
                        }
                    }

                    var columnError = "";

                    if (abundanceColIndex < 0) columnError = "Isos files does not have column: abundance";
                    else if (mzColIndex < 0) columnError = "Isos files does not have column: mz";
                    else if (scanColIndex < 0) columnError = "Isos files does not have column: scan_num";
                    else if (chargeColIndex < 0) columnError = "Isos files does not have column: charge";

                    if (!string.IsNullOrEmpty(columnError))
                    {
                        OnErrorEvent(columnError);
                        if (ThrowEvents)
                            throw new Exception(columnError);
                        return isosData;
                    }

                    // fill the rest of the table
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line))
                            continue;

                        var dataValues = line.Split(splitChars);

                        var abundance = GetValueDbl(dataValues, abundanceColIndex, 0);
                        var mz = GetValueDbl(dataValues, mzColIndex, 0);
                        var scan = GetValueInt(dataValues, scanColIndex, 0);
                        var charge = GetValueInt(dataValues, chargeColIndex, 0);

                        var isosEntry = new IsosData(scan, mz, abundance, charge);

                        isosData.Add(isosEntry);
                    }
                }
                return isosData;
            }
            catch (Exception ex)
            {
                OnErrorEvent("The file failed to load: " + ex.Message, ex);
                return null;
            }
        }

        protected double GetValueDbl(string[] dataValues, int colIndex, double valueIfMissing)
        {
            if (double.TryParse(dataValues[colIndex], out var value))
                return value;

            return valueIfMissing;
        }

        protected int GetValueInt(string[] dataValues, int colIndex, int valueIfMissing)
        {
            if (int.TryParse(dataValues[colIndex], out var value))
                return value;

            return valueIfMissing;
        }
    }
}
