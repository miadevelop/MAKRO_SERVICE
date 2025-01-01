using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using log4net;
using log4net.Config;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace MAKRO_SERVICE
{
    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));
        private static readonly string _processLogFilePath = Path.Combine(Directory.GetCurrentDirectory(), "process_log.json");
        private static ProcessLog _ProcessLog;
        private static readonly object fileLock = new object();
        private static ProcessLogManager _ProcessLogManager;

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

        static void Main(string[] args)
        {
            XmlConfigurator.Configure(new FileInfo("log4net.config"));
            log.Info("MAKRO_SERVICE started");

            //Dictionary<string, KeyMappingEntry> key_mapping
            KeyMapping keyMapping = LoadKeyMapping();
            if (keyMapping == null)
            {
                log.Error("Fehler beim Laden des Key Mappings.");
                return;
            }
            initProcessLogManager(keyMapping);
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

            string applicationName = Path.GetFileName(entry.execution);

            if (findRunningApplicationAndSwitchTo(entry, applicationName))
            {
                return;
            }

            ProcessInfo processInfoLastRun = _ProcessLogManager.GetProcessInfo(applicationName);

            if (processInfoLastRun != null)
            {
                log.Info($"Processinfo LastRun: {processInfoLastRun.ProcessId}, {processInfoLastRun.ProgramName}, {processInfoLastRun.LastRun}, {processInfoLastRun.Counter} ");
            }



            ExecuteProgram(entry.execution);
        }

        private static void initProcessLogManager(KeyMapping keyMapping)
        {
            log.Info($"Process log: {_processLogFilePath}");
            _ProcessLogManager = new ProcessLogManager(_processLogFilePath, keyMapping);          

        }

        private static bool findRunningApplicationAndSwitchTo(KeyMappingEntry entry, string applicationName)
        {
            bool result = false;
            var id = FindProcessIdByName(applicationName);
            if (id != null)
            {
                try
                {
                    int currentProcessID = id.Value;
                    log.Info($"switch to: {currentProcessID}");
                    ProcessInfo oldInfo = _ProcessLogManager.GetProcessInfo(applicationName);
                    int counter = oldInfo.Counter;
                    counter = counter + 1;
                    ProcessInfo newProcess = new ProcessInfo
                    {
                        ProcessId = currentProcessID,
                        ProgramName = applicationName,
                        ExecutionPath = entry.execution,
                        LastRun = DateTime.Now,
                        Counter = counter
                    };
                    log.Info($"aktualisiere Processinfo: {newProcess.ProcessId}, {newProcess.ProgramName}, {newProcess.ExecutionPath}, {newProcess.LastRun}, {newProcess.Counter}");
                    _ProcessLogManager.PutProcessInfo(newProcess);
                    result = SwitchToProcess(currentProcessID);

                    return result;

                }
                catch (Exception ex)
                {
                    log.Error(ex.Message);
                }
            }
            return result;
        }

        static void ExecuteProgram(string execution)
        {

            try
            {
                string programFileName = Path.GetFileName(execution);
                string programDirectory = Path.GetDirectoryName(execution);

                log.Info($"Starte ExecuteProgram für {programFileName}, in {programDirectory}");
                if (!string.IsNullOrEmpty(programDirectory))
                {
                    Directory.SetCurrentDirectory(programDirectory);
                    log.Info($"Verzeichnis gewechselt zu: {programDirectory}");
                }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = execution,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Maximized,
                    WorkingDirectory = programDirectory,
                };


                using (var process = Process.Start(processStartInfo))
                {
                    ProcessInfo oldInfo = _ProcessLogManager.GetProcessInfo(programFileName);
                    int counter = oldInfo.Counter;
                    counter = counter + 1;


                    ProcessInfo newProcess = new ProcessInfo
                    {
                        ProcessId = process.Id,
                        ProgramName = programFileName,
                        ExecutionPath = execution,
                        LastRun = DateTime.Now,
                        Counter = counter
                    };
                    log.Info($"aktualisiere Processinfo: {newProcess.ProcessId}, {newProcess.ProgramName}, {newProcess.ExecutionPath}, {newProcess.LastRun}, {newProcess.Counter}");
                    _ProcessLogManager.PutProcessInfo(newProcess);
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
            }

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
                KeyMapping keyMapping = JsonSerializer.Deserialize<KeyMapping>(jsonString);
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
                return new KeyInfo { Keycode = keycode, Action = action };
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

        public static bool SwitchToProcess(int processId)
        {
            bool result = false;
            try
            {
                Process process = Process.GetProcessById(processId);
                IntPtr mainWindowHandle = process.MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    ShowWindowAsync(mainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(mainWindowHandle);
                    result = true;
                    return result;
                }
                else
                {
                    log.Info("Das Hauptfenster des Prozesses wurde nicht gefunden.");
                    return result;
                }
            }
            catch (ArgumentException)
            {
                log.Error("Es wurde kein Prozess mit der angegebenen ID gefunden.");
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Wechseln zum Prozess: {ex.Message}");
                return result;
            }
            return result;
        }
    }
}