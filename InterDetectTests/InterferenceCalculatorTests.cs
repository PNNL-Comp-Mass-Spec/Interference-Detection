﻿using System;
using System.Collections.Generic;
using System.IO;
using InterDetect;
using NUnit.Framework;
using PRISM;

namespace InterDetectTests
{
    [TestFixture]
    public class InterferenceCalculatorTests
    {
        // Ignore Spelling: Samwise, Interdetect, Isos

        private int mFileCountCurrent;

        [OneTimeSetUp]
        public void Setup()
        {
            mFileCountCurrent = 0;
        }

        [Test]
        public void ChargeStateGuesstimatorTests()
        {
            // QC_Shew_13_04_A_17Feb14_Samwise_13-07-28.raw
            // Scan 10444
            var peakData = new[,]
            {
                {
                    425.1318, 425.2246, 425.3246, 425.7262, 425.8277, 426.2270, 426.2576, 426.5674, 426.7279, 427.1373, 427.2435, 427.7440, 427.7566,
                    427.8592, 428.1396, 428.2476, 428.2842, 428.5414, 428.5824, 428.7096, 428.8763, 428.9168, 428.9915, 429.1323, 429.2101, 429.2461,
                    429.5267, 429.5430, 429.5849, 429.7198, 429.7468, 429.8809, 429.9171, 430.2465, 430.2755, 430.5529, 430.7320, 430.8937, 431.2347,
                    431.2847, 431.7379, 432.2219, 432.2559, 432.2886, 432.7720, 433.2072, 433.2257, 433.2482, 433.2742, 433.4994, 433.5577, 433.7092,
                    433.7552, 433.7735, 433.9080, 434.2086, 434.2533, 434.5426, 434.5867, 434.7582, 434.8781, 435.2176, 435.2310, 435.2567, 435.2713,
                    435.7575, 435.8704, 436.2299, 436.2638, 436.5596, 436.7633, 436.8934, 437.2263, 437.2607, 437.5610, 437.5841, 437.7093, 437.7252,
                    437.7624, 437.8369, 437.8958, 438.2521, 438.5685, 438.7484, 438.9024, 439.2188, 439.2402, 439.7196, 439.7419, 439.9000, 440.2343,
                    440.2720, 440.6951, 440.7499, 440.8834, 441.1970, 441.2516, 441.7334, 442.0025, 442.1926, 442.2161, 442.2628, 442.2756, 442.6968,
                    442.9995, 443.2828, 443.7350, 443.7693, 443.7856, 443.9040, 444.2370, 444.2534, 444.2704, 444.3166, 444.4797, 444.7381, 444.7583,
                    444.7723, 444.8885, 445.0033, 445.1188, 445.1475, 445.2237, 445.2404, 445.2616, 445.6918, 445.7244, 445.7515, 445.9156, 446.1185,
                    446.1509, 446.1940, 446.2239, 446.2741, 446.5585, 446.7244, 446.7401, 446.8937, 447.1434, 447.1990, 447.3454, 447.5335, 447.7539,
                    447.8663, 448.1993, 448.2132, 448.2554, 448.7386, 448.7564, 449.2082, 449.2640, 449.5334, 449.5985, 449.7102, 449.7532, 449.7710,
                    449.8678, 449.9330, 449.9838, 450.2386, 450.2674, 450.4873, 450.6023, 450.7425, 450.7656, 450.8925, 450.9890, 451.2271, 451.2447,
                    451.5610, 451.7448, 451.7665, 451.8948, 452.2347, 452.2690, 452.7538, 452.7693, 452.8853, 452.8997, 452.9386, 453.0072, 453.0186,
                    453.2336, 453.2556, 453.2717, 453.5678, 453.7318, 453.7682, 453.8561, 454.2526, 454.2697, 454.5312, 454.5863, 454.7563, 454.7699,
                    454.8671, 454.9224, 455.2589, 455.5767, 455.7591, 455.9120, 456.2109, 456.3145, 456.5060, 457.1693, 457.2725, 457.3844, 457.4196,
                    457.5226, 457.6346, 457.6694, 457.7734, 457.8849, 457.9231, 458.0237, 458.2553, 458.2798, 458.3058, 458.3670, 458.5260, 458.7570,
                    458.7835, 458.8877, 459.2226, 459.2574, 459.2852, 459.3096, 459.5430, 459.5564, 459.7479, 459.8903, 460.2494, 460.3105, 460.4720,
                    460.7506, 460.8953, 460.9755, 461.2236, 461.2510, 461.7453, 461.7595, 461.9194, 470.2708, 470.4998, 470.6283, 470.7514, 470.7870,
                    470.8700, 470.9863, 471.0015, 471.2049, 471.2364, 471.2524, 471.2744, 471.7863, 471.8959, 472.0124, 472.2302, 472.3458, 472.4906,
                    472.5645, 472.7430, 472.8983, 473.2010, 473.2328, 473.2683, 473.2830, 473.5266, 473.7710, 474.2029, 474.2312, 474.2646, 474.2866,
                    474.7589, 475.2246, 475.2601, 475.5055, 475.7621, 475.7903, 475.8395, 475.9021, 476.0023, 476.0403, 476.2369, 476.2606, 476.5991,
                    476.6173, 476.7276, 476.7527, 476.8867, 476.9339, 477.2285, 477.2536, 477.2894, 477.7561, 477.7717, 477.8866, 478.2155, 478.2539,
                    478.2727, 478.5482, 478.7783, 530.2513, 530.2683, 530.2865, 530.3461, 530.5002, 530.5868, 530.7523, 530.7897, 530.9200, 531.0027,
                    531.2531, 531.2864, 531.5151, 532.0651, 532.2524, 533.2289, 533.2654, 533.3027, 533.7719, 534.0226, 534.2867, 534.3016, 534.3334,
                    534.6355, 534.7885, 534.9685, 535.2895, 535.3210, 535.7682, 536.7832, 537.2621, 537.5897, 537.7770, 537.8230, 537.9429, 538.2977,
                    638.3538, 638.8513, 639.0632, 639.3137, 639.5651, 639.6785, 639.8378, 640.0126, 640.3458, 640.6814, 640.7100, 640.7905, 640.8193,
                    641.2529, 641.2960, 641.3173, 641.8231, 641.9468, 642.3088, 642.3702, 642.4261, 642.8102, 642.8721, 643.3069, 643.3711, 643.4304,
                    643.8718, 643.9987, 644.3360, 645.3186, 645.8345, 646.3154, 716.8245, 717.3560, 717.4295, 717.8929, 718.3936, 718.4316, 719.3260,
                    719.3956, 719.4306, 719.6917, 719.8273, 719.8872, 720.0255, 720.3607, 720.3926, 721.3983, 722.3235, 722.3724, 722.8261, 722.8743,
                    723.3322, 723.3744, 723.9084, 724.3833, 798.8577, 799.3634, 800.4841, 800.9453, 801.4498, 801.9485, 802.4328, 802.6824, 804.8806,
                    805.4457, 857.4833, 858.4856, 859.8997, 877.4706, 878.4707, 880.3828, 881.3853, 882.3901, 985.5007, 987.5195, 988.5219, 989.4995,
                    990.5023, 992.5216, 1105.5085, 1106.4717, 1106.5244, 1106.9813, 1108.5516, 1109.549
                },
                {
                    114391.7, 21066024.0, 91852.4, 10038668.0, 96486.3, 2576349.3, 98095.9, 109201.5, 516117.2, 660924.7, 760028.9, 747391.4, 90611.5,
                    297280.0, 116129.3, 278515.7, 373192.4, 2665977.3, 3053968.5, 93714.4, 1827020.0, 3152899.0, 140777.6, 93494.1, 372258.9,
                    2587589.8, 127450.0, 158883.6, 357854.8, 125281.0, 883135.7, 479431.8, 94755.7, 178237.0, 1224231.0, 140226.1, 669624.4,
                    1059505.6, 330560.4, 2550244.3, 96506.0, 172047.6, 104567.2, 629375.0, 953661.7, 472050.0, 159302.6, 695769.3, 143040.3, 476358.4,
                    98638.2, 150112.5, 174602.5, 86440.3, 140630.9, 573374.9, 348812.3, 429474.9, 151388.7, 348119.1, 97137.2, 109371.6, 137900.1,
                    227136.1, 555855.6, 315111.4, 112989.8, 141710.4, 1209186.3, 6062332.0, 708267.9, 3519391.0, 1502165.1, 416161.5, 262643.7,
                    126849.7, 106666.5, 120195.2, 130943.6, 96904.7, 121962.4, 179811.1, 413206.9, 438131.6, 293469.5, 160064.6, 1135269.9, 81001.7,
                    506465.1, 459098.8, 178954.7, 110950.4, 1037828.4, 1256887.0, 127526.5, 375974.4, 489410.4, 108293.3, 126605.8, 545826.8, 90992.3,
                    127384.4, 2023494.9, 276656.3, 105277.7, 890971.8, 10148534.0, 834083.7, 375650.3, 145120.6, 5108306.0, 239598.8, 414618.8,
                    148393.9, 104715.3, 1518034.6, 423015.0, 109864.5, 546900.3, 151079.9, 349181.5, 2472832.5, 553677.5, 369967.0, 121853.0,
                    158203.8, 2271109.0, 102436.5, 137813.3, 88686.7, 435954.3, 75958.4, 593087.5, 104438.4, 392808.0, 402477.3, 129279.5, 100070.7,
                    131086.9, 2880812.3, 135615.1, 1632514.0, 3106269.8, 1425244.6, 471519.6, 1325903.6, 1943467.4, 234764.8, 559378.4, 518526.4,
                    9354598.0, 395202.6, 9239935.0, 378224.1, 99729.9, 352034.0, 585968.3, 2880717.8, 288799.7, 166824.4, 1428759.3, 535547.9,
                    301613.9, 1188930.0, 525447.5, 9821976.0, 566995.9, 7003513.5, 663375.8, 2447688.5, 169640.0, 477770.6, 1219305.9, 428941.0,
                    153401.5, 1761807.5, 1697150.3, 148584.3, 466452.3, 86390.2, 920330.7, 859889.3, 477703.3, 1108440.3, 585154.9, 403508.9,
                    172158.4, 175696.1, 90915.9, 293640.2, 822718.8, 351558.8, 97017.1, 4136919.0, 276819.7, 141632.7, 160655.7, 2013228.8, 385244.0,
                    739937.4, 442650.7, 129042.6, 280525.5, 83870.0, 551217.2, 61235628.0, 481663.4, 488908.9, 54151200.0, 408100.8, 158469.3,
                    33531636.0, 129090.7, 139536.8, 12642183.0, 13409791.0, 3559712.5, 1024051.5, 93243.7, 935751.9, 5555591.5, 1507996.0, 3767196.0,
                    2608785.5, 1334527.0, 499728.4, 154082.4, 90675.4, 1867469.9, 13291601.0, 417172.7, 6405871.0, 106763.6, 98224.1, 1346179.4,
                    147131.8, 137651.0, 113841.9, 342829.9, 1592952.8, 179149.6, 115343.5, 2792959.8, 663814.6, 346778.5, 1034447.4, 162221.1,
                    302418.1, 102065.6, 757718.7, 103150.7, 267344.2, 678861.8, 909715.9, 125250.7, 20301812.0, 127934.4, 13754410.0, 154605.5,
                    340999.5, 6730834.5, 146784.8, 2051277.5, 147950.2, 517617.8, 1210029.3, 280167.7, 90389.8, 611072.9, 306253.8, 128144.4,
                    1393386.5, 100667.5, 5501521.0, 368403.3, 2528271.8, 110840.1, 921523.3, 116328.2, 102702.4, 128147.7, 92446.8, 116432.3,
                    296585.6, 463064.6, 430846.3, 125964.8, 137752.4, 1291400.8, 117866.5, 156040.3, 122695.4, 745114.1, 389223.1, 441041.3,
                    1566824.4, 126730.4, 131930.4, 83110.0, 639418.2, 111705.9, 417645.9, 3349199.8, 650193.8, 323189.1, 130925.5, 734474.3,
                    1945091.0, 1148439.6, 126250.0, 862737.1, 131024.1, 647161.5, 126048.9, 132491.4, 114579.9, 134114.0, 171503.8, 357044.0,
                    134386.8, 620400.8, 118398.7, 767329.8, 834394.9, 108749.7, 700866.9, 351890.1, 541829.5, 441135.9, 315949.4, 159253.2, 452370.1,
                    140724.7, 140365.6, 123470.0, 162434.5, 130066.7, 768454.6, 523813.8, 182273.4, 481227.6, 1004026.1, 791494.2, 3775694.0,
                    2151909.8, 4965176.0, 2303662.0, 901409.7, 162831.5, 192086.1, 529796.2, 173753.7, 266103.8, 518913.2, 176498.5, 418176.8,
                    1854808.3, 3273145.0, 1605332.3, 1311058.1, 2550290.3, 537423.1, 1339837.9, 332276.8, 333451.8, 376149.2, 538057.8, 144091.5,
                    1106709.0, 825853.3, 151134.9, 474095.6, 2369401.0, 161549.3, 998608.6, 899669.3, 654600.5, 326759.4, 293215.2, 279065.3,
                    342173.9, 356676.4, 338306.3, 199567.0, 3962160.8, 1376288.0, 410837.1, 341940.0, 323003.5, 179628.3, 156315.5, 529641.6,
                    290659.0, 176935.4, 537646.1, 565570.8, 632907.4, 799535.8, 706276.2, 257872.6, 270945.2, 167614.7, 149762.4, 307525.3, 1064925.0,
                    675704.6, 165665.7, 319614.9, 325706.3, 4872849.5, 2474897.5, 544772.6, 145894.7, 586347.3, 449736.0, 868566.5, 437553.1,
                    141254.5, 667890.9, 146048.7, 301573.8, 484631.4, 250985.1, 163282.0
                }
            };

            var data = new List<PrecursorInfo>
            {
                new PrecursorInfo(428.9168, 2.0, 3) {ActualMass = 428.5828},
                new PrecursorInfo(436.5596, 2.0, 3) {ActualMass = 436.5596},
                new PrecursorInfo(443.7350, 2.0, 2) {ActualMass = 443.7351},
                new PrecursorInfo(450.8925, 2.0, 3) {ActualMass = 450.8925},
                new PrecursorInfo(454.7563, 2.0, 2) {ActualMass = 454.7563},
                new PrecursorInfo(457.2725, 2.0, 4) {ActualMass = 457.2725},
                new PrecursorInfo(474.7589, 2.0, 2) {ActualMass = 474.7588},
                new PrecursorInfo(534.3016, 2.0, 3) {ActualMass = 534.3016},
                new PrecursorInfo(642.3702, 2.0, 2) {ActualMass = 642.3702},
                new PrecursorInfo(720.3926, 2.0, 1) {ActualMass = 720.3926},
                new PrecursorInfo(800.9453, 2.0, 2) {ActualMass = 800.9453},
                new PrecursorInfo(857.4833, 2.0, 1) {ActualMass = 857.4835},
                new PrecursorInfo(880.3828, 2.0, 1) {ActualMass = 880.3826},
                new PrecursorInfo(989.4995, 2.0, 1) {ActualMass = 989.4995},
                new PrecursorInfo(1108.5516, 2.0, 1) {ActualMass = 1108.5516},
                new PrecursorInfo(447.7539, 2.0, -1),
                new PrecursorInfo(449.2640, 2.0, -1)
            };

            var peakList = InterferenceCalculator.ConvertToPeaks(ref peakData);
            foreach (var pre in data)
            {
                var guessCharge = InterferenceCalculator.ChargeStateGuesstimator(pre.IsolationMass, peakList);
                Console.WriteLine("Peak mass {0:F4} Charge: Thermo: {1} Guess: {2}", pre.IsolationMass, pre.ChargeState, guessCharge);
                if (pre.ChargeState > 0)
                {
                    Assert.AreEqual(pre.ChargeState, guessCharge, "Peak mass {0:F4} Charge: Thermo: {1} Guess: {2}", pre.IsolationMass, pre.ChargeState, guessCharge);
                }
            }
        }

        [Test]
        [TestCase(641.68, 2, 3, 0.9498275,
            "639.7077,167199|640.0714,169516|640.3343,156139|640.3762,123602|640.6768,194863|640.7348,103064|640.8257,133304|641.0148,2440679|641.3479,2129230|641.6791,883178|641.8780,86945|642.0114,459777|642.3540,207854|642.8660,169017|643.3537,222952|643.6028,221999|")]
        [TestCase(583.66, 2, 3, 0.360472,
            "581.6601,172507|581.8665,452705|581.9954,151606|582.3293,4048004|582.6630,3479459|582.9980,2128995|583.3313,1151475|583.6556,1625434|583.8265,102997|583.9895,1482809|584.0642,65116|584.3242,796772|584.6605,456975|584.7721,153606|584.8549,100397|584.9973,1992716|585.2829,71041|585.3314,1901800|585.5741,65218|585.6661,770890|")]
        public void TestInterferenceCalculator(
            double isolationMass, double isolationWidth,
            int chargeState, double expectedInterference,
            string peaks)
        {
            // Extract the data from peaks
            var peaksToParse = peaks.Split('|');
            var peakList = new List<Peak>();

            foreach (var peak in peaksToParse)
            {
                var charIndex = peak.IndexOf(',');
                if (charIndex <= 0)
                    continue;

                var mzText = peak.Substring(0, charIndex);
                var intensityText = peak.Substring(charIndex + 1);

                if (!double.TryParse(mzText, out var mz))
                    continue;

                if (double.TryParse(intensityText, out var intensity))
                {
                    var newPeak = new Peak
                    {
                        Mz = mz,
                        Abundance = intensity
                    };

                    peakList.Add(newPeak);
                }
            }

            var precursorInfo = new PrecursorInfo(isolationMass, isolationWidth, chargeState);

            var interferenceCalc = new InterferenceCalculator();
            interferenceCalc.Interference(precursorInfo, peakList);

            Assert.AreEqual(expectedInterference, precursorInfo.Interference, 0.01,
                            "Computed interference did not match expected: {0:F4} vs. {1:F4}", expectedInterference, precursorInfo.Interference);

            Console.WriteLine("Computed interference score of {0:F3} for {1:F4} m/z, charge {2}",
                precursorInfo.Interference, precursorInfo.IsolationMass, precursorInfo.ChargeState);
        }

        [Test]
        [TestCase(@"\\proto-2\UnitTest_Files\Interdetect", "Sample_4065_iTRAQ", "DLS201204031741_Auto822622", 20007, 21000,
            "20100=0.4913|20125=0.9613|20150=0.3088|20175=0.8689|20200=0.5874|20275=0.4388|20300=0.8917|20325=1|20350=0.959|20375=0.915|20400=0.5616|20425=0.9258|20450=0.9929|20500=0.4717|20550=0.9512|20575=0.8419|20675=1|20725=1|20750=1|20775=0.8774|20800=0.8458|20825=1|20850=0.5537|20900=0.947|20950=1|20975=0.8914|21000=1|")]
        [TestCase(@"\\proto-2\UnitTest_Files\Interdetect", "Sample_4065_iTRAQ", "", 20007, 21000,
            "20100=0.4913|20125=0.9613|20150=0.3088|20175=0.8689|20200=0.5874|20275=0.4388|20300=0.8917|20325=1|20350=0.959|20375=0.915|20400=0.5616|20425=0.9258|20450=0.9929|20500=0.4717|20550=0.9512|20575=0.8419|20618=0|20675=1|20725=1|20750=1|20775=0.8774|20800=0.8458|20825=1|20850=0.5537|20900=0.947|20950=1|20975=0.8914|21000=1|")]
        [TestCase(@"\\proto-2\UnitTest_Files\Interdetect", "Sample_5065_iTRAQ", "DLS201204031733_Auto822617", 39006, 40000,
            "39025=0.8276|39050=0.96|39125=0.7461|39175=0.9026|39200=0.8072|39225=0.8402|39250=0.8496|39275=1|39300=0.7802|39325=0.812|39350=0.824|39400=0.8676|39425=0.978|39475=0.9172|39500=0.9794|39525=0.777|39575=0.9728|39625=1|39675=0.9776|39700=0.5995|39725=0.894|39775=0.6856|39850=0.8273|39875=0.8846|39900=1|39925=0.9748|39950=0.6835|39975=0.6819|40000=0.8458|")]
        [Category("PNL_Domain")]
        public void TestVOrbiData(
            string storageDirectoryPath, string datasetName,
            string deconToolsResultsDirectoryName,
            int scanStart, int scanEnd,
            string expectedResultsByScan)
        {
            var datasetDirectory = new DirectoryInfo(Path.Combine(storageDirectoryPath, datasetName));

            var datasetFile = new FileInfo(Path.Combine(datasetDirectory.FullName, datasetName + ".raw"));

            if (!datasetDirectory.Exists)
                Assert.Fail("Dataset directory not found: " + datasetDirectory.FullName);

            if (!datasetFile.Exists)
                Assert.Fail(".Raw file not found: " + datasetFile.FullName);

            string isosFilePath;
            if (string.IsNullOrEmpty(deconToolsResultsDirectoryName))
            {
                // Isos file does not exist
                // Parent ions that do not have a charge state reported by the Thermo reader
                // will have their charge guesstimated by InterferenceCalculator.ChargeStateGuesstimator
                isosFilePath = string.Empty;
            }
            else
            {
                var isosFile = new FileInfo(Path.Combine(datasetDirectory.FullName, deconToolsResultsDirectoryName, datasetName + "_isos.csv"));
                isosFilePath = isosFile.Exists ? isosFile.FullName : string.Empty;
            }

            mFileCountCurrent++;

            var idm = new InterferenceDetector { ShowProgressAtConsole = false };
            RegisterEvents(idm);

            var precursorInfo = idm.ParentInfoPass(mFileCountCurrent, 2, datasetFile.FullName, isosFilePath, scanStart, scanEnd);

            if (precursorInfo == null)
            {
                Assert.Fail(datasetFile.FullName + " failed to load");
            }

            var resultsFile = new FileInfo("IDMResults_" + datasetName + ".txt");
            if (resultsFile.Exists)
            {
                try
                {
                    resultsFile.Delete();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning, could not delete results file before writing new results: " + ex.Message);
                }
            }

            idm.ExportInterferenceScores(precursorInfo, "Dataset" + mFileCountCurrent, resultsFile.FullName);

            Console.WriteLine("Results written to " + resultsFile.FullName);

            // Extract the data from expectedResultsByScan
            var expectedResultsToParse = expectedResultsByScan.Split('|');
            var expectedResults = new Dictionary<int, double>();

            foreach (var expectedResult in expectedResultsToParse)
            {
                var charIndex = expectedResult.IndexOf('=');
                if (charIndex <= 0)
                    continue;

                var scanNumberText = expectedResult.Substring(0, charIndex);
                var scoreText = expectedResult.Substring(charIndex + 1);

                if (!int.TryParse(scanNumberText, out var scanNumber))
                    continue;

                if (double.TryParse(scoreText, out var score))
                {
                    expectedResults.Add(scanNumber, score);
                }
            }

            var matchCount = 0;
            var comparisonCount = 0;

            foreach (var result in precursorInfo)
            {
                if (!expectedResults.TryGetValue(result.ScanNumber, out var expectedScore))
                {
                    continue;
                }

                comparisonCount++;

                if (Math.Abs(expectedScore - result.Interference) > 0.01)
                {
                    Console.WriteLine("Score mismatch for scan {0}: {1:F2} vs. {2:F2}", result.ScanNumber, expectedScore, result.Interference);
                }
                else
                {
                    matchCount++;
                }
            }

            if (matchCount == comparisonCount)
            {
                Console.WriteLine("Validated interference scores for {0} precursors", matchCount);
            }
            else
            {
                Assert.Fail("{0} / {1} precursors had unexpected interference scores", comparisonCount - matchCount, comparisonCount);
            }
        }

        protected void RegisterEvents(EventNotifier oProcessingClass)
        {
            oProcessingClass.StatusEvent += OnStatusEvent;
            oProcessingClass.ErrorEvent += OnErrorEvent;
            oProcessingClass.WarningEvent += OnWarningEvent;
        }

        private void OnWarningEvent(string message)
        {
            ConsoleMsgUtils.ShowWarning("Warning: " + message);
        }

        private void OnErrorEvent(string message, Exception ex)
        {
            ConsoleMsgUtils.ShowError("Error: " + message, ex);
        }

        private void OnStatusEvent(string message)
        {
            Console.WriteLine(message);
        }
    }
}
