@echo off
echo Running SOSDM Performance Test...
cd /d "src\bin\Release\net6.0"
echo ingest sample_data > test_commands.txt
echo status >> test_commands.txt
echo query machine learning >> test_commands.txt
echo quit >> test_commands.txt
SOSDM.exe < test_commands.txt
del test_commands.txt
pause
