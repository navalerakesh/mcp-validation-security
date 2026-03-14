using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Authentication.Strategies
{
    public abstract class BaseCliStrategy
    {
        protected readonly ILogger _logger;

        protected BaseCliStrategy(ILogger logger)
        {
            _logger = logger;
        }

        protected async Task<string?> RunCliCommandAsync(string executable, string arguments, CancellationToken ct, bool isInteractive = false)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = !isInteractive
            };

            if (isInteractive)
            {
                processStartInfo.RedirectStandardOutput = false;
                processStartInfo.RedirectStandardError = false;
                processStartInfo.RedirectStandardInput = false;
            }
            else
            {
                processStartInfo.RedirectStandardOutput = true;
                processStartInfo.RedirectStandardError = true;
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                processStartInfo.FileName = "cmd.exe";
                processStartInfo.Arguments = $"/c {executable} {arguments}";
            }

            try 
            {
                using var process = new Process { StartInfo = processStartInfo };
                process.Start();
                
                Task<string>? outputTask = null;
                Task<string>? errorTask = null;

                if (!isInteractive)
                {
                    outputTask = process.StandardOutput.ReadToEndAsync(ct);
                    errorTask = process.StandardError.ReadToEndAsync(ct);
                }
                
                await process.WaitForExitAsync(ct);

                if (process.ExitCode == 0)
                {
                    return isInteractive ? "success" : await outputTask!;
                }
                else
                {
                    if (!isInteractive)
                    {
                        var error = await errorTask!;
                        _logger.LogDebug($"{executable} command failed: {error}");
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Failed to run {executable}");
                return null;
            }
        }
    }
}
