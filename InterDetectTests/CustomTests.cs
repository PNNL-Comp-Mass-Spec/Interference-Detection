using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InterDetect;
using NUnit.Framework;

namespace IDM_Console
{
    class Class1
    {

        #region NUnit Tests

        [Test]
        public void DatabaseCheck()
        {
            var idm = new InterferenceDetector { ShowProgressAtConsole = false };

            if (!idm.Run(@"C:\DMS_WorkDir\Step_1_ASCORE"))
            {
                Console.WriteLine("You Fail");
            }
        }

        [Test]
        public void TestSisiData()
        {
            var decon = new string[] {@"\\proto-9\VOrbiETD02\2012_2\Sample_4065_iTRAQ\DLS201204031741_Auto822622\Sample_4065_iTRAQ_isos.csv",
                @"\\proto-9\VOrbiETD02\2012_2\Sample_5065_iTRAQ\DLS201204031733_Auto822617\Sample_5065_iTRAQ_isos.csv",
                @"\\proto-9\VOrbiETD02\2012_2\Sample_4050_iTRAQ_120330102958\DLS201204031744_Auto822624\Sample_4050_iTRAQ_120330102958_isos.csv"};
            var rawfiles = new string[] {@"\\proto-9\VOrbiETD02\2012_2\Sample_4065_iTRAQ\Sample_4065_iTRAQ.raw",
                @"\\proto-9\VOrbiETD02\2012_2\Sample_5065_iTRAQ\Sample_5065_iTRAQ.raw",
                @"\\proto-9\VOrbiETD02\2012_2\Sample_4050_iTRAQ_120330102958\Sample_4050_iTRAQ_120330102958.raw"};

            var idm = new InterferenceDetector { ShowProgressAtConsole = false };


            var filesToProcess = 1;

            for (var i = 0; i < filesToProcess; i++)
            {

                var lstPrecursorInfo = idm.ParentInfoPass(i + 1, filesToProcess, rawfiles[i], decon[i]);
                if (lstPrecursorInfo == null)
                {
                    Console.WriteLine(rawfiles[i] + " failed to load.  Deleting temp and aborting!");
                    return;
                }
                idm.ExportInterferenceScores(lstPrecursorInfo, "number", @"C:\Users\aldr699\Documents\2012\iTRAQ\InterferenceTesting\DataInt" + i + "efz50.txt");
            }
        }

        #endregion
    }
}
