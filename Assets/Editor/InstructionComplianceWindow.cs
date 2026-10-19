using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class InstructionComplianceWindow : EditorWindow
{
    private const int MaxAgents = 20;
    private static readonly string[] Teams = { "Red", "Blue", "Green", "Yellow" };
    private readonly InstructionCompliance[,] settings = new InstructionCompliance[Teams.Length, MaxAgents];
    private Vector2 scroll;

    [MenuItem("Tools/Pole Battle/Instruction Compliance")]
    public static void Open()
    {
        InstructionComplianceWindow window = GetWindow<InstructionComplianceWindow>("Instruction Compliance");
        window.minSize = new Vector2(650f, 460f);
        window.LoadSettings();
        window.Show();
    }

    private void OnEnable() { LoadSettings(); }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Leader Instruction Compliance by Team", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Configure each team and avatar independently. Save before Play mode. " +
            "Values remain fixed during the 10 practice and 100 official matches.", MessageType.Info);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Avatar", GUILayout.Width(65f));
            for (int t = 0; t < Teams.Length; t++)
                EditorGUILayout.LabelField(Teams[t], EditorStyles.boldLabel, GUILayout.Width(125f));
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < MaxAgents; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(65f));
                for (int t = 0; t < Teams.Length; t++)
                    settings[t, i] = (InstructionCompliance)EditorGUILayout.EnumPopup(settings[t, i], GUILayout.Width(125f));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Balanced preset")) ApplyBalancedPreset();
            if (GUILayout.Button("Copy Red to all")) CopyRedToAll();
            if (GUILayout.Button("Reload")) LoadSettings();
            if (GUILayout.Button("Save")) SaveSettings();
            EditorGUILayout.EndHorizontal();
        }

        if (EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Stop Play mode before changing the next session.", MessageType.Warning);
    }

    private void LoadSettings()
    {
        ApplyBalancedPreset();
        string path = GetSettingsPath();
        if (!File.Exists(path)) return;

        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            string[] parts = line.Split('=');
            if (parts.Length != 2) continue;

            InstructionCompliance compliance;
            if (!Enum.TryParse(parts[1].Trim(), true, out compliance)) continue;
            int number;
            string key = parts[0].Trim();
            if (int.TryParse(key, out number) && number >= 1 && number <= MaxAgents)
            {
                for (int t = 0; t < Teams.Length; t++) settings[t, number - 1] = compliance;
                continue;
            }

            for (int t = 0; t < Teams.Length; t++)
            {
                string prefix = Teams[t] + "-";
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(key.Substring(prefix.Length), out number) && number >= 1 && number <= MaxAgents)
                    settings[t, number - 1] = compliance;
            }
        }
        Repaint();
    }

    private void SaveSettings()
    {
        List<string> lines = new List<string>
        {
            "# Leader instruction compliance by team and avatar number.",
            "# Full = every directive, Half = every other directive, None = ignore directives."
        };
        for (int t = 0; t < Teams.Length; t++)
            for (int i = 0; i < MaxAgents; i++)
                lines.Add(Teams[t] + "-" + (i + 1) + "=" + settings[t, i]);

        File.WriteAllLines(GetSettingsPath(), lines.ToArray());
        AssetDatabase.Refresh();
        ShowNotification(new GUIContent("Team settings saved"));
    }

    private void ApplyBalancedPreset()
    {
        InstructionCompliance[] pattern = { InstructionCompliance.Full, InstructionCompliance.Half, InstructionCompliance.None };
        for (int t = 0; t < Teams.Length; t++)
            for (int i = 0; i < MaxAgents; i++) settings[t, i] = pattern[i % pattern.Length];
    }

    private void CopyRedToAll()
    {
        for (int t = 1; t < Teams.Length; t++)
            for (int i = 0; i < MaxAgents; i++) settings[t, i] = settings[0, i];
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(Application.dataPath, "InstructionCompliance.txt");
    }
}
