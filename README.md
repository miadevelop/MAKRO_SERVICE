# MAKRO_SERVICE

## Projektübersicht
**MAKRO_SERVICE** ist eine C#-Anwendung, die es ermöglicht, Tastenkombinationen (Key Mappings) mit spezifischen Programmausführungen zu verknüpfen.
Das Programm kann:
- Eingaben analysieren und die entsprechenden Aktionen ausführen,
- Prozesse im System überwachen und steuern,
- Logs für die Prozessüberwachung und Debugging erstellen.

Die Anwendung unterstützt die Automatisierung von Aufgaben durch benutzerdefinierte Zuordnungen von Tasteneingaben zu Programmen.

## Hauptfunktionen
- **Key Mapping:** Verknüpfen von Keycodes mit Programmen und deren Beschreibung.
- **Prozessmanagement:**
  - Starten von Programmen basierend auf Keycodes.
  - Wiederaufnahme und Verwaltung bereits laufender Prozesse.
  - Speicherung von Prozessinformationen in einer JSON-Datei (`process_log.json`).
- **Log-System:** Logging aller Aktivitäten mit der Bibliothek `log4net`.
- **Benutzerdefinierte Verzögerungen:** Steuerung der Verzögerung zwischen wiederholten Aktionen.

## Dateistruktur
- **`key_mapping.json`:** Enthält die Zuordnung von Keycodes zu Programmen und deren Beschreibungen.
- **`process_log.json`:** Dokumentiert die laufenden Prozesse und deren Informationen.
- **`log4net.config`:** Konfigurationsdatei für das Logging-System.

## Anforderungen
### Software:
- .NET Framework oder .NET Core
- log4net

### Zusätzliche Dateien:
- `key_mapping.json`: JSON-Datei zur Konfiguration der Tastenkombinationen.

### JSON-Beispiel für `key_mapping.json`:
```json
{
  "key_mapping": {
    "123": {
      "description": "Öffnet den Browser",
      "execution": "C:\\Program Files\\Mozilla Firefox\\firefox.exe",
      "application": "Firefox"
    },
    "124": {
      "description": "Startet den Editor",
      "execution": "C:\\Windows\\System32\\notepad.exe",
      "application": "Notepad"
    }
  }
}
```

## Installation und Nutzung
1. **Voraussetzungen:**
   - Stellen Sie sicher, dass .NET installiert ist.
   - Erstellen Sie die notwendigen Konfigurationsdateien (`key_mapping.json`, `log4net.config`).

2. **Build und Ausführung:**
   - Kompilieren Sie das Projekt mit Visual Studio oder der .NET CLI.
   - Platzieren Sie die Datei `key_mapping.json` im gleichen Verzeichnis wie die Anwendung.

3. **Eingaben:**
   - Das Programm kann mit einem Keycode gestartet werden:
     ```bash
     MAKRO_SERVICE.exe keycode:123
     ```
   - Ohne Argumente werden alle Key Mappings im Log aufgelistet.

## Fehlerbehebung
1. **Fehlendes Key Mapping:**
   - Prüfen Sie, ob `key_mapping.json` korrekt formatiert ist.
2. **Ungültige Eingabe:**
   - Stellen Sie sicher, dass der Keycode im richtigen Format übergeben wird (z. B. `keycode:123`).
3. **Logging-Probleme:**
   - Kontrollieren Sie die Konfiguration in `log4net.config`.

## Erweiterbarkeit
Das Projekt kann leicht erweitert werden:
- Zusätzliche Funktionen für Key Mapping,
- Integration weiterer Konfigurationsdateien,
- Erweiterte Prozessüberwachung oder Analyse-Tools.

## Lizenz
Dieses Projekt ist Open Source und steht unter der [MIT-Lizenz](LICENSE).

---
**Hinweis:** Diese Readme ist darauf ausgelegt, die wichtigsten Informationen für Benutzer und Entwickler bereitzustellen. Weitere technische Details finden Sie in der Dokumentation des Codes.

