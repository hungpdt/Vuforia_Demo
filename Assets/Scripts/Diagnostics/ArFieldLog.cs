// Diagnostic file logger for on-site AR testing.
// Written for the museum trip: the device is far from any PC, so nothing may
// depend on a USB cable or an open adb logcat window.
using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Appends diagnostic lines to a text file inside
/// <see cref="Application.persistentDataPath"/>, which on Android is
/// /sdcard/Android/data/&lt;package&gt;/files/ - visible over USB/MTP and to any
/// file manager, no root needed.
///
/// Unity warnings, errors and exceptions are mirrored into the same file, so an
/// exception thrown three rooms away lands right next to the tracking timeline
/// that led to it.
///
/// Deliberately a static class: the log must survive a scene reload and must be
/// writable from anywhere without wiring a reference.
/// </summary>
public static class ArFieldLog
{
    // Flush often. A crash or a force-quit must not cost more than a few lines.
    const int kFlushEveryLines = 10;

    static StreamWriter s_Writer;
    static int s_LinesSinceFlush;
    static bool s_LogHookInstalled;
    static float s_T0;

    /// <summary>Full path of the file being written, or null before Open().</summary>
    public static string FilePath { get; private set; }

    public static bool IsOpen => s_Writer != null;

    /// <summary>Folder to browse on the device to collect everything afterwards.</summary>
    public static string Folder => Application.persistentDataPath;

    /// <summary>
    /// Most recent error or exception, surfaced by the HUD. Unity's own
    /// development console renders at a fixed tiny size on a high-density
    /// phone screen, which is unreadable in the field - so the HUD reprints it.
    /// </summary>
    public static string LastError { get; private set; }

    public static int ErrorCount { get; private set; }

    public static void ClearErrors()
    {
        LastError = null;
        ErrorCount = 0;
    }

    /// <summary>
    /// Opens a new log file. Safe to call more than once - later calls are
    /// ignored so two components can both try without splitting the timeline.
    /// </summary>
    public static void Open(string tag)
    {
        if (s_Writer != null)
            return;

        s_T0 = Time.realtimeSinceStartup;

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        FilePath = Path.Combine(Application.persistentDataPath,
                                "ARLOG_" + stamp + "_" + Sanitize(tag) + ".txt");

        try
        {
            s_Writer = new StreamWriter(FilePath, false);
        }
        catch (Exception e)
        {
            // Never let logging break the app we are trying to diagnose.
            Debug.LogError("ArFieldLog: cannot open " + FilePath + " - " + e.Message);
            return;
        }

        // Install once only. The hook outlives scene loads, the writer may not.
        if (!s_LogHookInstalled)
        {
            Application.logMessageReceived += OnUnityLog;
            s_LogHookInstalled = true;
        }
    }

    public static void Close()
    {
        if (s_Writer == null)
            return;

        Line("--- log closed ---");
        try
        {
            s_Writer.Flush();
            s_Writer.Dispose();
        }
        catch (Exception) { /* nothing useful left to do while shutting down */ }

        s_Writer = null;
    }

    /// <summary>One timestamped line. Timestamps are seconds since Open().</summary>
    public static void Line(string text)
    {
        if (s_Writer == null)
            return;

        try
        {
            s_Writer.WriteLine("[{0,8:F2}] {1}", Time.realtimeSinceStartup - s_T0, text);
        }
        catch (Exception)
        {
            return;
        }

        if (++s_LinesSinceFlush >= kFlushEveryLines)
            Flush();
    }

    /// <summary>Visually separated block, for the header and for snapshots.</summary>
    public static void Section(string title)
    {
        Line("");
        Line("===== " + title + " =====");
    }

    public static void Flush()
    {
        if (s_Writer == null)
            return;

        try
        {
            s_Writer.Flush();
            s_LinesSinceFlush = 0;
        }
        catch (Exception) { /* disk full or handle gone - keep running regardless */ }
    }

    // Mirror Unity's own messages. Plain Debug.Log is skipped on purpose:
    // NavRuntimeMonitor prints a multi-line block every second and would bury
    // the tracking timeline.
    static void OnUnityLog(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Log)
            return;

        Line("UNITY " + type + ": " + message);

        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        {
            ErrorCount++;

            // First stack frame only: enough to locate the call site on screen,
            // short enough to stay readable on a phone.
            var firstFrame = stackTrace ?? "";
            var newline = firstFrame.IndexOf('\n');
            if (newline > 0)
                firstFrame = firstFrame.Substring(0, newline);

            LastError = message + "\n" + firstFrame.Trim();

            Line(stackTrace);
            Flush();   // an exception is exactly the moment the app may die
        }
    }

    static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "session";

        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '-');

        return s.Replace(' ', '-');
    }
}
