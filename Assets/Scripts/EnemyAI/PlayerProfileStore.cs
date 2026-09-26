using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 玩家画像的跨会话保存（实验分支）：每个场景一个 JSON 文件，
/// Application.persistentDataPath/EnemyAIProfiles/场景名.json。
/// PlayerBehaviourMonitor 在开始时读取，在每波结束、游戏暂停和退出时写入。
///
/// 文件记录区域名称：分区设置变了（区域不一致）时不使用旧文件，从零开始，下次保存时覆盖。
/// 开关：EnemyAISettings.persistPlayerProfile（菜单 Tools > Enemy AI > Persist Player Profile）。
/// 训练场不读写（ArenaEnvironment 关闭监测组件的 PersistProfile）。
/// </summary>
public static class PlayerProfileStore
{
    public const int FormatVersion = 1;

    [Serializable]
    public class SavedProfile
    {
        public int formatVersion = FormatVersion;
        public string scene;
        public string savedAt;
        [Tooltip("累计参与过的游戏会话数（含保存时的这一次）")]
        public int sessions;
        public string[] zones;
        public PlayerProfileState profile;
    }

    public static string Folder => Path.Combine(Application.persistentDataPath, "EnemyAIProfiles");

    public static string PathFor(string scene)
    {
        string name = string.IsNullOrEmpty(scene) ? "untitled" : scene;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return Path.Combine(Folder, name + ".json");
    }

    /// <summary>
    /// 读取该场景保存的画像。没有文件时返回 null 且 error 为 null；
    /// 文件损坏、版本或区域不符时返回 null，error 说明原因
    /// </summary>
    public static SavedProfile Load(string scene, string[] zones, out string error)
    {
        error = null;
        string path = PathFor(scene);
        if (!File.Exists(path))
            return null;

        SavedProfile saved;
        try
        {
            saved = JsonUtility.FromJson<SavedProfile>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            error = $"无法读取 {path}: {exception.Message}";
            return null;
        }

        if (saved == null || saved.profile == null)
            error = $"{path} 内容为空";
        else if (saved.formatVersion != FormatVersion)
            error = $"{path} 的格式版本 {saved.formatVersion} ≠ {FormatVersion}";
        else if (!SameZones(saved.zones, zones))
            error = $"{path} 的区域划分与当前场景不同（分区设置改过）";
        return error == null ? saved : null;
    }

    /// <summary>保存（先写临时文件再替换，中途退出不会留下损坏的文件）</summary>
    public static bool Save(string scene, string[] zones, int sessions, PlayerProfile profile, out string error)
    {
        error = null;
        string path = PathFor(scene);
        try
        {
            Directory.CreateDirectory(Folder);
            var saved = new SavedProfile
            {
                scene = scene,
                savedAt = DateTime.Now.ToString("o"),
                sessions = sessions,
                zones = zones,
                profile = profile.ExportState()
            };
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(saved, true));
            File.Copy(temporary, path, true);
            File.Delete(temporary);
            return true;
        }
        catch (Exception exception)
        {
            error = $"无法写入 {path}: {exception.Message}";
            return false;
        }
    }

    private static bool SameZones(string[] a, string[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

#if UNITY_EDITOR
    private const string MenuPersist = "Tools/Enemy AI/Persist Player Profile";

    [UnityEditor.MenuItem(MenuPersist, false, 120)]
    private static void TogglePersist()
    {
        EnemyAISettings settings = EnemyAISettings.Load();
        if (settings == null)
        {
            Debug.LogWarning("[PlayerProfileStore] 找不到 EnemyAISettings（Resources）；没有设置资产时画像默认跨会话保存。" +
                             "可用 Assets > Create > Enemy AI > Enemy AI Settings 在 Resources 文件夹中创建");
            return;
        }
        settings.persistPlayerProfile = !settings.persistPlayerProfile;
        UnityEditor.EditorUtility.SetDirty(settings);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log(settings.persistPlayerProfile
            ? $"[PlayerProfileStore] 玩家画像跨会话保存：开（{Folder}）"
            : "[PlayerProfileStore] 玩家画像跨会话保存：关（每次进入游戏从零开始；已保存的文件保留）");
    }

    [UnityEditor.MenuItem(MenuPersist, true)]
    private static bool ValidateTogglePersist()
    {
        UnityEditor.Menu.SetChecked(MenuPersist, EnemyAISettings.PersistPlayerProfile);
        return true;
    }

    [UnityEditor.MenuItem("Tools/Enemy AI/Open Saved Player Profiles Folder", false, 121)]
    private static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        UnityEditor.EditorUtility.RevealInFinder(Folder);
    }

    [UnityEditor.MenuItem("Tools/Enemy AI/Delete Saved Player Profiles", false, 122)]
    private static void DeleteSaved()
    {
        string[] files = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json") : new string[0];
        if (files.Length == 0)
        {
            Debug.Log($"[PlayerProfileStore] 没有已保存的玩家画像（{Folder}）");
            return;
        }
        if (!UnityEditor.EditorUtility.DisplayDialog("删除已保存的玩家画像",
                $"删除 {files.Length} 个文件？下次进入游戏时敌人 AI 从零开始学习玩家。\n\n{Folder}", "删除", "取消"))
            return;
        foreach (string file in files)
            File.Delete(file);
        Debug.Log($"[PlayerProfileStore] 已删除 {files.Length} 个已保存的玩家画像");
    }
#endif
}
