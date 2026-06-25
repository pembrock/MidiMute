using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using MidiMute.Models;

namespace MidiMute.Services
{
    public sealed class AudioDeviceRestartService
    {
        private const int RestartDelaySeconds = 2;
        private const int EnableRetryCount = 5;
        private const int EnableRetryDelaySeconds = 2;

        public IReadOnlyList<AudioDeviceInfo> GetRestartableAudioDevices()
        {
            var devices = new List<AudioDeviceInfo>();

            using var searcher = new ManagementObjectSearcher(
                """
                SELECT Name, DeviceID, PNPClass
                FROM Win32_PnPEntity
                WHERE PNPClass = 'MEDIA'
                   OR PNPClass = 'AudioEndpoint'
                   OR PNPClass = 'Focusrite Audio'
                   OR Name LIKE '%Focusrite%'
                   OR Name LIKE '%Scarlett%'
                   OR DeviceID LIKE '%FOCUSRITE%'
                """);

            foreach (ManagementObject device in searcher.Get())
            {
                var name = device["Name"]?.ToString();
                var instanceId = device["DeviceID"]?.ToString();
                var deviceClass = device["PNPClass"]?.ToString();

                if (string.IsNullOrWhiteSpace(instanceId))
                    continue;

                devices.Add(new AudioDeviceInfo
                {
                    Name = string.IsNullOrWhiteSpace(name) ? instanceId : name,
                    InstanceId = instanceId,
                    DeviceClass = deviceClass ?? ""
                });
            }

            return devices
                .GroupBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public Task RestartDeviceAsync(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Audio device instance id is required.", nameof(instanceId));

            return Task.Run(() =>
            {
                var operationId = Guid.NewGuid().ToString("N");
                var scriptPath = Path.Combine(Path.GetTempPath(), $"MidiMuteRestartAudioDevice-{operationId}.ps1");
                var outputPath = Path.Combine(Path.GetTempPath(), $"MidiMuteRestartAudioDevice-{operationId}.log");
                File.WriteAllText(scriptPath, CreateRestartScript(instanceId, outputPath), Encoding.UTF8);

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                if (process == null)
                    throw new InvalidOperationException("Failed to start elevated audio device restart process.");

                process.WaitForExit();

                var output = File.Exists(outputPath)
                    ? File.ReadAllText(outputPath)
                    : "";

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Audio device restart exited with code {process.ExitCode}.{Environment.NewLine}" +
                        (string.IsNullOrWhiteSpace(output) ? "No restart output was captured." : output));
                }

                TryDelete(scriptPath);
                TryDelete(outputPath);
            });
        }

        private static string CreateRestartScript(string instanceId, string outputPath)
        {
            return $$"""
                $ErrorActionPreference = 'Continue'
                $OutputEncoding = [System.Text.Encoding]::UTF8
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                $deviceId = '{{EscapePowerShellSingleQuotedString(instanceId)}}'
                $outputPath = '{{EscapePowerShellSingleQuotedString(outputPath)}}'

                function Write-RestartLog {
                    param([string] $Message)
                    Add-Content -LiteralPath $outputPath -Value $Message -Encoding UTF8
                }

                function Invoke-LoggedCommand {
                    param(
                        [string] $Title,
                        [scriptblock] $Command
                    )

                    Write-RestartLog ">>> $Title"

                    try {
                        $output = & $Command 2>&1

                        foreach ($line in $output) {
                            Write-RestartLog $line.ToString()
                        }

                        Write-RestartLog "ExitCode=0"
                        Write-RestartLog ""
                        return 0
                    }
                    catch {
                        Write-RestartLog $_.Exception.ToString()
                        Write-RestartLog "ExitCode=1"
                        Write-RestartLog ""
                        return 1
                    }
                }

                function Invoke-LoggedPnPUtil {
                    param([string[]] $Arguments)

                    Write-RestartLog ">>> pnputil $($Arguments -join ' ')"
                    $output = & pnputil @Arguments 2>&1
                    $exitCode = $LASTEXITCODE

                    foreach ($line in $output) {
                        Write-RestartLog $line.ToString()
                    }

                    Write-RestartLog "ExitCode=$exitCode"
                    Write-RestartLog ""
                    return $exitCode
                }

                function Write-DeviceStatus {
                    param([string] $Prefix)

                    try {
                        $device = Get-PnpDevice -InstanceId $deviceId -ErrorAction Stop
                        Write-RestartLog "$Prefix Status=$($device.Status); Problem=$($device.Problem); Class=$($device.Class); FriendlyName=$($device.FriendlyName)"
                    }
                    catch {
                        Write-RestartLog "$Prefix Status unavailable: $($_.Exception.Message)"
                    }
                }

                function Enable-DeviceWithRetry {
                    for ($attempt = 1; $attempt -le {{EnableRetryCount}}; $attempt++) {
                        Write-RestartLog "Enable attempt $attempt/{{EnableRetryCount}}"
                        $exitCode = Invoke-LoggedCommand -Title "Enable-PnpDevice -InstanceId $deviceId" -Command {
                            Enable-PnpDevice -InstanceId $deviceId -Confirm:$false -ErrorAction Stop
                        }
                        Write-DeviceStatus "After enable attempt $attempt"

                        if ($exitCode -eq 0) {
                            return 0
                        }

                        Start-Sleep -Seconds {{EnableRetryDelaySeconds}}
                    }

                    return 1
                }

                Write-RestartLog "DeviceId=$deviceId"
                Write-DeviceStatus "Before disable"
                $disableExitCode = Invoke-LoggedCommand -Title "Disable-PnpDevice -InstanceId $deviceId" -Command {
                    Disable-PnpDevice -InstanceId $deviceId -Confirm:$false -ErrorAction Stop
                }
                Write-DeviceStatus "After disable"
                Start-Sleep -Seconds {{RestartDelaySeconds}}
                $enableExitCode = Enable-DeviceWithRetry

                if ($disableExitCode -ne 0 -or $enableExitCode -ne 0) {
                    Write-RestartLog "PowerShell PnpDevice cmdlets failed. Trying pnputil fallback."
                    $disableExitCode = Invoke-LoggedPnPUtil -Arguments @('/disable-device', $deviceId, '/force')
                    Start-Sleep -Seconds {{RestartDelaySeconds}}
                    for ($attempt = 1; $attempt -le {{EnableRetryCount}}; $attempt++) {
                        Write-RestartLog "pnputil enable attempt $attempt/{{EnableRetryCount}}"
                        $enableExitCode = Invoke-LoggedPnPUtil -Arguments @('/enable-device', $deviceId)
                        Write-DeviceStatus "After pnputil enable attempt $attempt"

                        if ($enableExitCode -eq 0) {
                            break
                        }

                        Start-Sleep -Seconds {{EnableRetryDelaySeconds}}
                    }
                }

                if ($disableExitCode -ne 0 -or $enableExitCode -ne 0) {
                    exit 1
                }

                exit 0
""";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Temporary diagnostics should not break the restart flow.
            }
        }

        private static string EscapePowerShellSingleQuotedString(string value)
            => value.Replace("'", "''", StringComparison.Ordinal);
    }
}
