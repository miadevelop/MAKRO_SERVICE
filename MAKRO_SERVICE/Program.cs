using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using log4net;
using log4net.Config;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;

namespace MAKRO_SERVICE
{

    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));
        private static readonly string processLogFilePath = Path.Combine(Directory.GetCurrentDirectory(), "process_log.json");
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly Dictionary<string, Task> _runningTasks = new Dictionary<string, Task>();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_SHOW = 5;
        private const int MillisecondsDelay = 1000;
        static async Task Main(string[] args)
        {
            XmlConfigurator.Configure(new FileInfo("log4net.config"));
            log.Info("MAKRO_SERVICE started");

            var keyMapping = LoadKeyMapping();
            if (keyMapping == null)
            {
                log.Error("Fehler beim Laden des Key Mappings.");
                return;
            }

            log.Info("Key Mapping erfolgreich geladen.");

            if (args.Length == 0)
            {
                displayMappingList(keyMapping);
                return;
            }

            string input = string.Join(" ", args);
            log.Info($"Eingabe: {input}");

            int keycode = ExtractKeycode(input);
            log.Info($"Extracted Keycode: {keycode}");

            if (keycode == -1 || !keyMapping.key_mapping.ContainsKey(keycode.ToString()))
            {
                log.Error($"Keycode '{keycode}' nicht gefunden im Key Mapping oder ungültige Eingabe.");
                return;
            }

            var entry = keyMapping.key_mapping[keycode.ToString()];
            log.Info($"Keycode: {keycode}, Beschreibung: {entry.description}, Ausführung: {entry.execution}, Anwendung: {entry.application}");
            await ExecuteProgramAsync(entry.execution, MillisecondsDelay);
        }


        static async Task ExecuteProgramAsync(string execution, int millisecondsDelay)
        {
            string programFileName = Path.GetFileName(execution);

            await _semaphore.WaitAsync();
            try
            {
                if (_runningTasks.TryGetValue(programFileName, out var existingTask) && !existingTask.IsCompleted)
                {
                    log.Info($"Programm '{programFileName}' wird bereits ausgeführt.");
                    return;
                }

                var task = ExecuteProgramInternalAsync(execution, millisecondsDelay);
                _runningTasks[programFileName] = task;
                await task;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        static async Task ExecuteProgramInternalAsync(string execution, int millisecondsDelay)
        {
            string programDirectory = Path.GetDirectoryName(execution);
            string programFileName = Path.GetFileName(execution);

            if (!string.IsNullOrEmpty(programDirectory))
            {
                Directory.SetCurrentDirectory(programDirectory);
                log.Info($"Verzeichnis gewechselt zu: {programDirectory}");
            }

            var existingProcess = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(programFileName))
                                         .FirstOrDefault(p => !p.HasExited);

            if (existingProcess != null)
            {
                ShowWindow(existingProcess.MainWindowHandle, SW_SHOW);
                SetForegroundWindow(existingProcess.MainWindowHandle);
                log.Info($"Fenster für bereits laufendes Programm '{programFileName}' in den Vordergrund geholt.");
                return;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = execution,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Maximized
            };

            try
            {
                using (var process = Process.Start(processStartInfo))
                {
                    if (process != null)
                    {
                        SaveProcessInfo(process.Id, programFileName, execution);
                        await process.WaitForExitAsync();
                    }
                }
                log.Info($"Programm gestartet und beendet: {execution}");
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Starten des Programms '{programFileName}': {ex.Message}");
            }
            finally
            {
                await Task.Delay(millisecondsDelay);
            }
        }

        static void SaveProcessInfo(int processId, string programName, string executionPath)
        {
            var processLog = LoadProcessLog();
            var existingProcessInfo = processLog.ProcessInfos.Find(p => p.ProgramName.Equals(programName, StringComparison.OrdinalIgnoreCase));

            if (existingProcessInfo != null)
            {
                existingProcessInfo.ProcessId = processId;
            }
            else
            {
                processLog.ProcessInfos.Add(new ProcessInfo
                {
                    ProcessId = processId,
                    ProgramName = programName,
                    ExecutionPath = executionPath
                });
            }

            string json = JsonSerializer.Serialize(processLog);
            File.WriteAllText(processLogFilePath, json);
        }

        static ProcessLog LoadProcessLog()
        {
            if (File.Exists(processLogFilePath))
            {
                string json = File.ReadAllText(processLogFilePath);
                return JsonSerializer.Deserialize<ProcessLog>(json) ?? new ProcessLog();
            }
            return new ProcessLog();
        }

        static void displayMappingList(KeyMapping keyMapping)
        {
            foreach (var entry in keyMapping.key_mapping)
            {
                log.Info($"Key: {entry.Key}, Beschreibung: {entry.Value.description}, Ausführung: {entry.Value.execution}, Anwendung: {entry.Value.application}");
            }
        }

        static KeyMapping LoadKeyMapping()
        {
            string keyMappingFilePath = Path.Combine(Directory.GetCurrentDirectory(), "key_mapping.json");
            if (!File.Exists(keyMappingFilePath))
            {
                log.Error("Die KeyMapping-Datei wurde nicht gefunden!");
                return null;
            }

            try
            {
                string jsonString = File.ReadAllText(keyMappingFilePath);
                var keyMapping = JsonSerializer.Deserialize<KeyMapping>(jsonString);
                if (keyMapping == null || keyMapping.key_mapping == null)
                {
                    log.Error("Fehler: Das Key Mapping konnte nicht deserialisiert werden.");
                    return null;
                }
                return keyMapping;
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Laden des Key Mappings: {ex.Message}");
                return null;
            }
        }

        static int ExtractKeycode(string input)
        {
            var match = Regex.Match(input, @"keycode:\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int keycode))
            {
                return keycode;
            }
            return -1;
        }
    }
}
