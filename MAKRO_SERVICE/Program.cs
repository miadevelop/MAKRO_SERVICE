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

namespace MAKRO_SERVICE;

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

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    private const int WM_COMMAND = 0x111;
    private const int SC_MINIMIZE = 0xF020;

    public static void MinimizeAllWindows()
    {
        IntPtr lHwnd = FindWindow("Shell_TrayWnd", null);
        SendMessage(lHwnd, WM_COMMAND, (IntPtr)SC_MINIMIZE, IntPtr.Zero);
    }

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const int MillisecondsDelay = 1000;
    const string ACTION = "pressed";

    static void Main(string[] args)
    {


        XmlConfigurator.Configure(new FileInfo("log4net.config"));
        log.Info("MAKRO_SERVICE started");
        int processes = getCurrentApplicationProcesses();
        log.Info($"Number instances: {processes}");
        if (processes >= 2)
        {
            log.Info("Application already running");

        }

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
            ApplicationTerminate();
            return;
        }

        ProcessInfo processInfoLastRun = _ProcessLogManager.GetProcessInfo(applicationName);

        if (processInfoLastRun != null)
        {
            log.Info($"Processinfo LastRun: {processInfoLastRun.ProcessId}, {processInfoLastRun.ProgramName}, {processInfoLastRun.LastRun}, {processInfoLastRun.Counter} ");
        }

        bool isInTimeSpan = IsLastRunInTimeSpan(processInfoLastRun, MillisecondsDelay);
        log.Info($"IsLastRunInTimeSpan: {isInTimeSpan}");

        if (isInTimeSpan)
        {
            log.Info("IsLastRunInTimeSpan, Run abborted.");
            return;
        }

        ExecuteProgram(entry.execution);
       
    }

    private static void ApplicationTerminate()
    {
        log.Info("Makroservice beendet");
        Environment.Exit(0); 
    }

    private static int getCurrentApplicationProcesses()
    {
        string processName = Process.GetCurrentProcess().ProcessName;
        var runningProcesses = Process.GetProcessesByName(processName);
        return runningProcesses.Length;
    }

    private static void initProcessLogManager(KeyMapping keyMapping)
    {
        log.Info($"Process log: {_processLogFilePath}");
        _ProcessLogManager = new ProcessLogManager(_processLogFilePath, keyMapping);
    }

    private static bool findRunningApplicationAndSwitchTo(KeyMappingEntry entry, string applicationName)
    {
        log.Info("Starting: findRunningApplicationAndSwitchTo");
        bool result = false;
        int currentProcessID = 0;
        ProcessInfo oldInfo = _ProcessLogManager.GetProcessInfo(applicationName);
        int oldProcessID = oldInfo.ProcessId;

        if (IsProcessIdRunning(oldProcessID))
        {
            currentProcessID = oldProcessID;
        }
        else
        {
            string windowsTitle = oldInfo.ProgramName;
            string appName = System.IO.Path.GetFileNameWithoutExtension(applicationName);
            var id0 = FindProcessIdByName(appName);
            var id1 = FindProcessIdByWindowTitle(windowsTitle);
            currentProcessID = id0 ?? id1 ?? -1;
        }

        if (currentProcessID != -1)
        {
            try
            {
                log.Info($"switch to: {currentProcessID}");
                int counter = oldInfo.Counter + 1;
                ProcessInfo newProcess = new ProcessInfo
                {
                    ProcessId = currentProcessID,
                    ProgramName = oldInfo.ProgramName,
                    ExecutionPath = oldInfo.ExecutionPath,
                    LastRun = DateTime.Now,
                    Counter = counter
                };
                log.Info($"aktualisiere Processinfo: {newProcess.ProcessId}, {newProcess.ProgramName}, {newProcess.ExecutionPath}, {newProcess.LastRun}, {newProcess.Counter}");
                _ProcessLogManager.PutProcessInfo(newProcess);
                result = SwitchToProcess(currentProcessID);
                Thread.Sleep(MillisecondsDelay);
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
                int counter = oldInfo.Counter + 1;

                ProcessInfo newProcess = new ProcessInfo
                {
                    ProcessId = process.Id,
                    ProgramName = oldInfo.ProgramName,
                    ExecutionPath = oldInfo.ExecutionPath,
                    LastRun = DateTime.Now,
                    Counter = counter
                };
                log.Info($"aktualisiere Processinfo: {newProcess.ProcessId}, {newProcess.ProgramName}, {newProcess.ExecutionPath}, {newProcess.LastRun}, {newProcess.Counter}");
                _ProcessLogManager.PutProcessInfo(newProcess);
                Thread.Sleep(MillisecondsDelay);
            }
        }
        catch (Exception ex)
        {
            log.Error(ex.Message);
        }
        finally {
            ApplicationTerminate();
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
        if (processInfoLastRun == null) return false;

        TimeSpan timeSinceLastRun = DateTime.Now - processInfoLastRun.LastRun;
        return timeSinceLastRun.TotalMilliseconds <= milliseconds;
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

    public static int? FindProcessIdByWindowTitle(string windowTitle)
    {
        try
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                if (process.MainWindowTitle.ToLower().Contains(windowTitle.ToLower()))
                {
                    return process.Id;
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Suchen des Fenstertitels: {ex.Message}");
            return null;
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
        try
        {
            MinimizeAllWindows();
            System.Threading.Thread.Sleep(500);

            Process process = Process.GetProcessById(processId);
            IntPtr mainWindowHandle = process.MainWindowHandle;
            if (mainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(mainWindowHandle, SW_RESTORE);
                SetForegroundWindow(mainWindowHandle);
                BringWindowToTop(mainWindowHandle);
                SetFocus(mainWindowHandle);

                log.Info($"Prozess mit ID {processId} erfolgreich in den Vordergrund gebracht.");
                return true;
            }
            else
            {
                log.Info($"Das Hauptfenster des Prozesses mit ID {processId} wurde nicht gefunden.");
                return false;
            }
        }
        catch (Exception ex)
        {
            log.Error($"Fehler beim Wechseln zum Prozess mit ID {processId}: {ex.Message}");
            return false;
        }
    }
}