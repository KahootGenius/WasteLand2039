using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 伏击老虎机的跨会话保存（自适应尸潮，实验分支）：与玩家画像在同一个文件夹，每个场景一个文件，
/// Application.persistentDataPath/EnemyAIProfiles/场景名.bandit.json。
/// PredictiveCommander（placement = Bandit）在第一波开始时读取，在每波结束、游戏暂停和退出时写入；
/// 只在玩家画像也跨会话保存时读写（PlayerBehaviourMonitor.ProfileIsPersistent：EnemyAISettings.persistPlayerProfile 开启，
/// 且不是训练场）。
///
/// 模式（按扇区 / 共享）、距离档或臂数与当前设置不符时不使用旧文件（AmbushBandit.TryImportState 检查），
/// 从先验开始，下次保存时覆盖。
/// 删除：菜单 Tools > Enemy AI > Delete Saved Player Profiles 删除该文件夹里所有 .json，包括这些文件。
/// </summary>
public static class AmbushBanditStore
{
    public const int FormatVersion = 1;
    public const string FileSuffix = ".bandit.json";

    [Serializable]
    public class SavedBandit
    {
        public int formatVersion = FormatVersion;
        public string scene;
        public string savedAt;
        [Tooltip("累计参与过的游戏会话数（含保存时的这一次）")]
        public int sessions;
        [Tooltip("保存时的指挥官（DisplayName，便于查看）")]
        public string commander;
        public AmbushBanditState bandit;
    }

    public static string PathFor(string scene)
    {
        string name = string.IsNullOrEmpty(scene) ? "untitled" : scene;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return Path.Combine(PlayerProfileStore.Folder, name + FileSuffix);
    }

    /// <summary>
    /// 读取该场景保存的老虎机。没有文件时返回 null 且 error 为 null；文件损坏或版本不符时返回 null，error 说明原因。
    /// 模式和距离档由调用方用 AmbushBandit.TryImportState 检查
    /// </summary>
    public static SavedBandit Load(string scene, out string error)
    {
        error = null;
        string path = PathFor(scene);
        if (!File.Exists(path))
            return null;

        SavedBandit saved;
        try
        {
            saved = JsonUtility.FromJson<SavedBandit>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            error = $"无法读取 {path}: {exception.Message}";
            return null;
        }

        if (saved == null || saved.bandit == null)
            error = $"{path} 内容为空";
        else if (saved.formatVersion != FormatVersion)
            error = $"{path} 的格式版本 {saved.formatVersion} ≠ {FormatVersion}";
        return error == null ? saved : null;
    }

    /// <summary>保存（先写临时文件再替换，中途退出不会留下损坏的文件）</summary>
    public static bool Save(string scene, int sessions, string commander, AmbushBandit bandit, out string error)
    {
        error = null;
        string path = PathFor(scene);
        try
        {
            Directory.CreateDirectory(PlayerProfileStore.Folder);
            var saved = new SavedBandit
            {
                scene = scene,
                savedAt = DateTime.Now.ToString("o"),
                sessions = sessions,
                commander = commander,
                bandit = bandit.ExportState()
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
}
