using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InterDetect
{
    public class InterferenceCalculator
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="precursorInfo"></param>
        /// <param name="spectraData2D">Array of size [2,x], where [0,0] is first m/z and [1,0] is first intensity</param>
        public static void Interference(PrecursorInfo precursorInfo, double[,] spectraData2D)
        {
            var mzToFindLow = precursorInfo.IsoloationMass - (precursorInfo.IsolationWidth);
            var mzToFindHigh = precursorInfo.IsoloationMass + (precursorInfo.IsolationWidth);

            var a = 0;
            var b = spectraData2D.GetUpperBound(1) + 1;
            var c = 0;

            var lowInd = BinarySearch(ref spectraData2D, a, b, c, mzToFindLow);
            var highInd = BinarySearch(ref spectraData2D, lowInd, b, c, mzToFindHigh);

            //capture all peaks in isowidth+buffer
            var peaks = ConvertToPeaks(ref spectraData2D, lowInd, highInd);

            var mzWindowLow = precursorInfo.IsoloationMass - (precursorInfo.IsolationWidth / 2);
            var mzWindowHigh = precursorInfo.IsoloationMass + (precursorInfo.IsolationWidth / 2);

            // Narrow the range of peaks to the final tolerances
            peaks = FilterPeaksByMZ(mzWindowLow, mzWindowHigh, peaks);

            //find target peak for use as precursor to find interference
            ClosestToTarget(precursorInfo, peaks);

            //perform the calculation
            InterferenceCalculation(precursorInfo, peaks);
        }

        /// <summary>
        /// Calculates interference with the precursor ion
        /// </summary>
        /// <param name="precursorInfo"></param>
        /// <param name="peaks"></param>
        private static void InterferenceCalculation(PrecursorInfo precursorInfo, List<Peak> peaks)
        {
            const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
            const double PreErrorAllowed = 10.0;
            double MaxPreInt = 0;
            double MaxInterfereInt = 0;
            double OverallInterference = 0;

            if (peaks.Count > 0)
            {
                for (var j = 0; j < peaks.Count; j++)
                {
                    var difference = (peaks[j].Mz - precursorInfo.ActualMass) * precursorInfo.ChargeState;
                    var difference_Rounded = Math.Round(difference);
                    var expected_difference = difference_Rounded * C12_C13_MASS_DIFFERENCE;
                    var Difference_ppm = Math.Abs((expected_difference - difference) /
                                                  (precursorInfo.IsoloationMass * precursorInfo.ChargeState)) * 1000000;

                    if (Difference_ppm < PreErrorAllowed)
                    {
                        MaxPreInt += peaks[j].Abundance;
                    }

                    MaxInterfereInt += peaks[j].Abundance;
                }
                OverallInterference = MaxPreInt / MaxInterfereInt;
            }
            else
            {
                Console.WriteLine("Did not find the precursor for " + precursorInfo.IsoloationMass + " in scan " + precursorInfo.ScanNumber);
            }

            precursorInfo.Interference = OverallInterference;
        }

        private static List<Peak> ConvertToPeaks(ref double[,] spectraData2D, int lowind, int highind)
        {
            var mzs = new List<Peak>();

            for (var i = lowind + 1; i <= highind - 1; i++)
            {
                if (spectraData2D[1, i] > 0)
                {
                    /*
                    // Old code: simplistic centroiding
                    var j = i;
                    var abusum = 0.0;
                    while (spectraData2D[1, i] > 0 && !(spectraData2D[1, i - 1] > spectraData2D[1, i] && spectraData2D[1, i + 1] > spectraData2D[1, i]))
                    {
                        abusum += spectraData2D[1, i];
                        i++;
                    }
                    var end = i;
                    i = j;
                    var peaksum = 0.0;
                    var peakmax = 0.0;
                    while (i != end)
                    {
                        //test using maximum of peak
                        if (spectraData2D[1, i] > peakmax)
                        {
                            peakmax = spectraData2D[1, i];
                        }

                        peaksum += spectraData2D[1, i] / abusum * spectraData2D[0, i];
                        i++;
                    }
                    var centroidPeak = new Peak
                    {
                        Mz = peaksum,
                        Abundance = peakmax
                    };
                    mzs.Add(centroidPeak);
                    */

                    mzs.Add(new Peak
                    {
                        Mz = spectraData2D[0, i],
                        Abundance = spectraData2D[1, i]
                    });
                }
            }

            return mzs;
        }

        private static int BinarySearch(ref double[,] spectraData2D, int a, int b, int c, double mzToFind)
        {
            const double tol = 0.1;

            while (true)
            {
                if (Math.Abs(spectraData2D[0, c] - mzToFind) < tol || c == (b + a) / 2)
                {
                    break;
                }
                c = (b + a) / 2;
                if (spectraData2D[0, c] < mzToFind)
                {
                    a = c;
                }
                if (spectraData2D[0, c] > mzToFind)
                {
                    b = c;
                }
            }
            return c;
        }

        /// <summary>
        /// finds the peak closest to the targeted peak to be isolated in raw header and
        /// gets the intensity info.
        /// </summary>
        /// <param name="precursorInfo"></param>
        /// <param name="peaks"></param>
        private static void ClosestToTarget(PrecursorInfo precursorInfo, IEnumerable<Peak> peaks)
        {
            var closest = 100000.0;
            var abund = 0.0;
            foreach (var p in peaks)
            {
                var temp = Math.Abs(p.Mz - precursorInfo.IsoloationMass);
                if (temp < closest)
                {
                    precursorInfo.ActualMass = p.Mz;
                    closest = temp;
                    abund = p.Abundance;
                }
            }

            if (precursorInfo is PrecursorIntense)
            {
                ((PrecursorIntense) precursorInfo).PrecursorIntensity = abund;
            }
        }

        /// <summary>
        /// Filters the peak list to only retain peaks between lowMz and highMz
        /// </summary>
        /// <param name="lowMz"></param>
        /// <param name="highMz"></param>
        /// <param name="peaks"></param>
        private static List<Peak> FilterPeaksByMZ(double lowMz, double highMz, IEnumerable<Peak> peaks)
        {
            var peaksFiltered = from peak in peaks
                where peak.Mz < highMz
                      && peak.Mz > lowMz
                orderby peak.Mz
                select peak;

            return peaksFiltered.ToList();
        }
    }
}
