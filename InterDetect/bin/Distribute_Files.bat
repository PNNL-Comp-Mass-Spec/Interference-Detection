xcopy Debug\InterDetect.dll "F:\Documents\Projects\DataMining\DMS_Managers\Analysis_Manager\AM_Common\" /D /Y
xcopy Debug\InterDetect.dll "F:\Documents\Projects\DataMining\DMS_Managers\Analysis_Manager\AM_Program\bin\" /D /Y
xcopy Debug\InterDetect.dll "F:\Documents\Projects\DataMining\DMS_Managers\Analysis_Manager\Plugins\AM_IDM_Plugin\AM_IDM_Plugin\bin\Debug\" /D /Y
xcopy Debug\InterDetect.dll "F:\Documents\Projects\DataMining\MASIC\bin\" /D /Y
xcopy Debug\InterDetect.dll "F:\Documents\Projects\DataMining\MASIC\bin\Console\Debug\" /D /Y
xcopy Debug\InterDetect.dll "F:\Documents\Projects\DataMining\MASIC\Lib\" /D /Y
xcopy Debug\InterDetect.dll "F:\Documents\Projects\DataMining\MASIC\MASICTest\bin\Debug\" /D /Y

xcopy Debug\InterDetect.pdb "F:\Documents\Projects\DataMining\DMS_Managers\Analysis_Manager\AM_Common\" /D /Y
xcopy Debug\InterDetect.pdb "F:\Documents\Projects\DataMining\DMS_Managers\Analysis_Manager\AM_Program\bin\" /D /Y
xcopy Debug\InterDetect.pdb "F:\Documents\Projects\DataMining\DMS_Managers\Analysis_Manager\Plugins\AM_IDM_Plugin\AM_IDM_Plugin\bin\Debug\" /D /Y
xcopy Debug\InterDetect.pdb "F:\Documents\Projects\DataMining\MASIC\bin\" /D /Y
xcopy Debug\InterDetect.pdb "F:\Documents\Projects\DataMining\MASIC\bin\Console\Debug\" /D /Y
xcopy Debug\InterDetect.pdb "F:\Documents\Projects\DataMining\MASIC\Lib\" /D /Y
xcopy Debug\InterDetect.pdb "F:\Documents\Projects\DataMining\MASIC\MASICTest\bin\Debug\" /D /Y

if not "%1"=="NoPause" pause
