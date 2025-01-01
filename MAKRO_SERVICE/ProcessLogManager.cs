using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace MAKRO_SERVICE
{
    // Manager-Klasse zur Handhabung von ProcessLog und Dateioperationen
    public class ProcessLogManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(ProcessLogManager));
        private readonly string _filePath;
        private Dictionary<string, ProcessInfo> _processInfoDictionary;
        private readonly KeyMapping _keyMapping;

        public ProcessLogManager(string filePath, KeyMapping keyMapping)
        {
            _filePath = filePath;
            _keyMapping = keyMapping;
            _processInfoDictionary = new Dictionary<string, ProcessInfo>();
            EnsureFileExists();
        }

        // Methode zum Hinzufügen oder Aktualisieren von ProcessInfo
        public void PutProcessInfo(ProcessInfo processInfo)
        {
            try
            {
                if (processInfo == null)
                {
                    throw new ArgumentNullException(nameof(processInfo));
                }

                _processInfoDictionary[processInfo.ProgramName] = processInfo;
                SaveProcessLog();
                log.Info($"ProcessInfo für '{processInfo.ProgramName}' wurde erfolgreich gespeichert.");
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Hinzufügen/Aktualisieren von ProcessInfo: {ex.Message}");
            }
        }

        // Methode zum Abrufen von ProcessInfo anhand der ProcessId
        public ProcessInfo GetProcessInfo(int processId)
        {
            try
            {
                var processInfo = _processInfoDictionary.Values.FirstOrDefault(p => p.ProcessId == processId);

                if (processInfo == null)
                {
                    log.Warn($"Kein ProcessInfo mit ProcessId '{processId}' gefunden.");
                }

                return processInfo;
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Abrufen von ProcessInfo anhand der ProcessId: {ex.Message}");
                return null;
            }
        }

        // Überladene Methode zum Abrufen von ProcessInfo anhand des ProgramNamens
        public ProcessInfo GetProcessInfo(string programName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(programName))
                {
                    throw new ArgumentException("ProgramName darf nicht null oder leer sein.", nameof(programName));
                }

                if (_processInfoDictionary.TryGetValue(programName, out var processInfo))
                {
                    return processInfo;
                }

                log.Warn($"Kein ProcessInfo für ProgramName '{programName}' gefunden.");
                return null;
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Abrufen von ProcessInfo anhand des ProgramNamens: {ex.Message}");
                return null;
            }
        }

        private Dictionary<string, ProcessInfo> LoadProcessLog()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    log.Error($"Die Datei '{_filePath}' wurde nicht gefunden.");
                    return new Dictionary<string, ProcessInfo>();
                }

                string json = File.ReadAllText(_filePath);
                var processInfoDictionary = JsonSerializer.Deserialize<Dictionary<string, ProcessInfo>>(json);

                if (processInfoDictionary == null)
                {
                    log.Warn("Die ProcessLog-Datei ist leer oder konnte nicht korrekt deserialisiert werden.");
                    return new Dictionary<string, ProcessInfo>();
                }

                return processInfoDictionary;
            }
            catch (JsonException ex)
            {
                log.Error($"Fehler beim Deserialisieren des ProcessLogs: {ex.Message}");
            }
            catch (Exception ex)
            {
                log.Error($"Allgemeiner Fehler beim Laden des ProcessLogs: {ex.Message}");
            }

            return new Dictionary<string, ProcessInfo>();
        }

        private void SaveProcessLog()
        {
            try
            {
                string json = JsonSerializer.Serialize(_processInfoDictionary, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Speichern der ProcessLog-Datei: {ex.Message}");
            }
        }

        private void EnsureFileExists()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    foreach (var item in _keyMapping.key_mapping)
                    {
                        KeyMappingEntry keyMappingEntry = item.Value;
                        ProcessInfo processInfo = new ProcessInfo
                        {
                            ProcessId = 0,
                            ProgramName = System.IO.Path.GetFileName(keyMappingEntry.execution),
                            ExecutionPath = keyMappingEntry.execution,
                            LastRun = DateTime.Now,
                            Counter = 0
                        };

                        if (!_processInfoDictionary.ContainsKey(processInfo.ProgramName))
                        {
                            _processInfoDictionary.Add(processInfo.ProgramName, processInfo);
                        }
                    }
                    SaveProcessLog();
                }
                else
                {
                    _processInfoDictionary = LoadProcessLog();
                }
            }
            catch (Exception ex)
            {
                log.Error($"Fehler beim Erstellen der ProcessLog-Datei: {ex.Message}");
            }
        }
    }


}
