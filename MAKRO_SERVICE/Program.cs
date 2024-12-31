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
        private static readonly string _processLogFilePath = Path.Combine(Directory.GetCurrentDirectory(), "process_log.json");
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly Dictionary<string, Task> _runningTasks = new Dictionary<string, Task>();
        private static ProcessLog _ProcessLog;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow); 
     

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int MillisecondsDelay = 1000;
        const string ACTION = "pressed";
       


        static async Task Main(string[] args)
        {
            XmlConfigurator.Configure(new FileInfo("log4net.config"));
            log.Info("MAKRO_SERVICE started");
            log.Info($"Process log: {_processLogFilePath}");

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

            
            KeyInfo keyInfo = ExtractKeyInfo(input);
            int keycode = keyInfo.Keycode;
           
            if (!keyInfo.Action.ToLower().Contains(ACTION))
            {
                log.Error($"Keycode '{keycode}', {keyInfo.Action}, abborted!");
                return;
            }

            log.Info($"Extracted Keycode: {keycode}");

            if (keycode == -1 || !keyMapping.key_mapping.ContainsKey(keycode.ToString()))
            {
                log.Error($"Keycode '{keycode}' nicht gefunden im Key Mapping oder ungültige Eingabe.");
                return;
            }

            var entry = keyMapping.key_mapping[keycode.ToString()];
            log.Info($"Keycode: {keycode}, Beschreibung: {entry.description}, Ausführung: {entry.execution}, Anwendung: {entry.application}");

            if (!System.IO.File.Exists(_processLogFilePath))
            {
                SaveProcessInfo(0, "", "");
            }
            _ProcessLog = LoadProcessLog();
            string applicationName=System.IO.Path.GetFileName(entry.execution);

            ProcessInfo processInfoLastRun = GetProcessLogInfoByName(applicationName);

            //IsLastRunInTimeSpan(ProcessInfo processInfoLastRun, int milliseconds)
           
            if( IsProcessIdRunning(processInfoLastRun.ProcessId))
            {
                log.Info($"Prozess läuft schon, IsProcessIdRunning: {processInfoLastRun.ProcessId}, {processInfoLastRun.ProgramName} ");
                SwitchToProcess(processInfoLastRun.ProcessId);
                SaveProcessInfo(processInfoLastRun.ProcessId, processInfoLastRun.ProgramName, processInfoLastRun.ExecutionPath);
                await Task.Delay(MillisecondsDelay);
                return;
            }

            int? currentProcessID = FindProcessIdByName(processInfoLastRun.ProgramName);
            if (currentProcessID.HasValue)
            {
                log.Info($"Prozess läuft schon, FindProcessIdByName: {currentProcessID.Value}, {processInfoLastRun.ProgramName}");
                SwitchToProcess(currentProcessID.Value);
                SaveProcessInfo(processInfoLastRun.ProcessId, processInfoLastRun.ProgramName, processInfoLastRun.ExecutionPath);
                await Task.Delay(MillisecondsDelay);
                return;
            }


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

                        // Verzögerung hinzufügen, um sicherzustellen, dass das Programm korrekt gestartet wurde
                        await Task.Delay(MillisecondsDelay);

                        // Fenster in den Vordergrund holen
                        ShowWindow(process.MainWindowHandle, SW_SHOW);
                        SetForegroundWindow(process.MainWindowHandle);

                        log.Info($"Programm gestartet: {execution}");
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
                existingProcessInfo.LastRun = DateTime.Now;
                existingProcessInfo.Counter++;
            }
            else
            {
                processLog.ProcessInfos.Add(new ProcessInfo
                {
                    ProcessId = processId,
                    ProgramName = programName,
                    ExecutionPath = executionPath,
                    LastRun = DateTime.Now,
                    Counter = 1
                });
            }

            string json = JsonSerializer.Serialize(processLog);
            File.WriteAllText(_processLogFilePath, json);
        }

        static ProcessLog LoadProcessLog()
        {
            try
            {
                if (File.Exists(_processLogFilePath))
                {
                    string json = File.ReadAllText(_processLogFilePath);
                    return JsonSerializer.Deserialize<ProcessLog>(json) ?? new ProcessLog();
                }
                return new ProcessLog();
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }
        public static ProcessInfo GetProcessLogInfoByName(string programName)
        {
            if (_ProcessLog == null || _ProcessLog.ProcessInfos == null)
            {
                return null;
            }

            return _ProcessLog.ProcessInfos.FirstOrDefault(p =>
                p.ProgramName.Equals(programName, StringComparison.OrdinalIgnoreCase));
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

        static KeyInfo ExtractKeyInfo(string input)
        {
            var keycodeMatch = Regex.Match(input, @"keycode:\s*(\d+)");
            var actionMatch = Regex.Match(input, @"action:\s*(pressed|released)");

            if (keycodeMatch.Success && actionMatch.Success)
            {
                int keycode = int.Parse(keycodeMatch.Groups[1].Value);
                string action = actionMatch.Groups[1].Value;

                return new KeyInfo
                {
                    Keycode = keycode,
                    Action = action
                };
            }

            return null;
        }

        public static bool IsLastRunInTimeSpan(ProcessInfo processInfoLastRun, int milliseconds)
        {
            if (processInfoLastRun == null)
            {
                return false;
            }

            DateTime currentTimestamp = DateTime.Now;
            DateTime lastRunPlusInterval = processInfoLastRun.LastRun.AddMilliseconds(milliseconds);

            return lastRunPlusInterval <= currentTimestamp;
        }

        public static bool IsProcessIdRunning(int processId)
        {
            try
            {
                return Process.GetProcesses().Any(p => p.Id == processId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static int? FindProcessIdByName(string name)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    return processes[0].Id;
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Suchen des Prozesses: {ex.Message}");
                return null;
            }
        }


        public static void SwitchToProcess(int processId)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
                IntPtr mainWindowHandle = process.MainWindowHandle;

                if (mainWindowHandle != IntPtr.Zero)
                {
                    ShowWindowAsync(mainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(mainWindowHandle);
                }
                else
                {
                   log.Info("Das Hauptfenster des Prozesses wurde nicht gefunden.");
                }
            }
            catch (ArgumentException)
            {
                log.Error("Es wurde kein Prozess mit der angegebenen ID gefunden.");
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Wechseln zum Prozess: {ex.Message}");
            }
        }

    }

   

}
