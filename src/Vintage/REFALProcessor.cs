    // REFAL-5λ Integration Helper
    public class REFALProcessor
    {
        private readonly string _refalExecutable;
        
        public REFALProcessor(string executablePath)
        {
            _refalExecutable = executablePath;
        }
        
        public async Task<string> ProcessText(string input, string ruleName)
        {
            try
            {
                // Create temporary input file
                var tempInput = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempInput, input);
                
                // Execute REFAL program
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _refalExecutable,
                        Arguments = $"{ruleName} {tempInput}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                // Cleanup
                File.Delete(tempInput);
                
                return output.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"REFAL processing error: {ex.Message}");
                return input; // Fallback to original input
            }
        }
    }