using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Survival.Client
{
    /// <summary>
    /// Shows errors/exceptions on screen (IMGUI, independent of game cameras and canvases) and appends
    /// every log line to persistentDataPath/orol-log.txt, so problems on a phone without a computer
    /// can be screenshotted and reported. Tap the panel to hide it.
    /// </summary>
    public sealed class ErrorOverlay : MonoBehaviour
    {
        private readonly List<string> _errors = new List<string>();
        private readonly object _lock = new object();
        private StreamWriter _file;
        private bool _hidden;
        private GUIStyle _style;

        public static string LogPath { get; private set; }

        private void Awake()
        {
            try
            {
                LogPath = Path.Combine(Application.persistentDataPath, "orol-log.txt");
                _file = new StreamWriter(LogPath, false) { AutoFlush = true };
                _file.WriteLine($"OROL log {DateTime.Now:O} | {Application.version} | {SystemInfo.deviceModel} | {SystemInfo.operatingSystem} | {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}");
            }
            catch
            {
                _file = null;
            }
            Application.logMessageReceivedThreaded += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            _file?.Dispose();
        }

        private void OnLog(string message, string stack, LogType type)
        {
            lock (_lock)
            {
                try
                {
                    _file?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{type}] {message}");
                    if (type == LogType.Exception) _file?.WriteLine(stack);
                }
                catch
                {
                    // never throw from the log handler
                }
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    string line = message.Length > 400 ? message.Substring(0, 400) : message;
                    if (type == LogType.Exception && !string.IsNullOrEmpty(stack))
                    {
                        string first = stack.Split('\n')[0];
                        line += "\n   at " + first;
                    }
                    _errors.Add(line);
                    if (_errors.Count > 6) _errors.RemoveAt(0);
                    _hidden = false;
                }
            }
        }

        private void OnGUI()
        {
            string text;
            lock (_lock)
            {
                if (_hidden || _errors.Count == 0) return;
                text = "ERROR (screenshot this and send it)\n\n" + string.Join("\n\n", _errors);
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                    fontSize = Mathf.Max(14, Screen.height / 45),
                };
                _style.normal.textColor = Color.white;
            }
            var rect = new Rect(Screen.width * 0.03f, Screen.height * 0.05f, Screen.width * 0.94f, Screen.height * 0.6f);
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.6f, 0f, 0f, 0.95f);
            if (GUI.Button(rect, text, _style)) _hidden = true;
            GUI.backgroundColor = old;
        }
    }
}
