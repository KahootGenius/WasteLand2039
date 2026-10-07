using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 评估程序（无界面批量评估用的独立程序，MLTraining/eval.sh 运行它）的构建：只包含训练场的评估场景，
/// 不修改游戏的 Build Settings。输出 MLTraining/builds/eval/MLArena_Eval.app。
/// 改了指挥官 / 训练场的代码或资产、或安装了新的 RL 模型之后重新构建。
///
/// 菜单：Tools > Enemy AI > Build Eval Player
/// 命令行（只能在 Unity 编辑器没有打开本项目时使用）：MLTraining/build_eval_player.sh
///   即 Unity -batchmode -projectPath . -executeMethod EvalPlayerBuilder.BuildFromCommandLine -logFile 日志
///
/// 构建可能会重新序列化两个与评估无关的资产（URP 设置会补上默认字段，TMP 后备字体会清空字形表）。
/// 构建前记下它们在磁盘上的内容，构建后若有变化则还原（相当于以前手动的 git checkout --）。
/// </summary>
public static class EvalPlayerBuilder
{
    public const string OutputPath = "MLTraining/builds/eval/MLArena_Eval.app";

    /// <summary>评估程序包含的场景。第一个是启动场景；ArenaManager 按命令行 -arenaScene 切换到其他场景</summary>
    public static readonly string[] Scenes =
    {
        "Assets/ML/Arena/MLArena_Eval_Baseline.unity",
        "Assets/ML/Arena/MLArena_Eval_RL.unity",
        "Assets/ML/Arena/MLArena_Eval_V2.unity",
        "Assets/ML/Arena/MLArena_Data.unity",
    };

    private static readonly string[] RestoreAfterBuild =
    {
        "Assets/Settings/UniversalRP.asset",
        "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset",
    };

    [MenuItem("Tools/Enemy AI/Build Eval Player")]
    private static void BuildMenu()
    {
        // 构建读取磁盘上的场景文件：评估场景有未保存的修改时先问是否保存
        bool evalSceneDirty = Enumerable.Range(0, SceneManager.sceneCount)
            .Select(SceneManager.GetSceneAt)
            .Any(scene => scene.isDirty && Scenes.Contains(scene.path));
        if (evalSceneDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (Build(out string summary))
            Debug.Log("[EvalPlayerBuilder] " + summary);
        else
            Debug.LogError("[EvalPlayerBuilder] " + summary);
    }

    /// <summary>-executeMethod 入口：构建后退出编辑器（成功 0，失败 1）</summary>
    public static void BuildFromCommandLine()
    {
        bool ok = Build(out string summary);
        Debug.Log("[EvalPlayerBuilder] " + summary);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static bool Build(out string summary)
    {
        string missing = Scenes.FirstOrDefault(scene => !File.Exists(scene));
        if (missing != null)
        {
            summary = $"找不到场景 {missing}（先运行 Tools > Enemy AI > Build ML Arena）";
            return false;
        }

        var snapshots = new Dictionary<string, byte[]>();
        foreach (string path in RestoreAfterBuild)
        {
            if (File.Exists(path))
                snapshots[path] = File.ReadAllBytes(path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            });
        }
        finally
        {
            RestoreAssets(snapshots);
        }

        BuildSummary result = report.summary;
        bool ok = result.result == BuildResult.Succeeded;
        summary = ok
            ? $"评估程序已构建：{OutputPath}（{result.totalSize / (1024 * 1024)} MB，{result.totalTime.TotalSeconds:F0} 秒，" +
              $"场景 {string.Join("、", Scenes.Select(Path.GetFileNameWithoutExtension))}）"
            : $"评估程序构建失败：{result.result}，{result.totalErrors} 个错误（详见 Console / 日志）";
        return ok;
    }

    private static void RestoreAssets(Dictionary<string, byte[]> snapshots)
    {
        foreach (var pair in snapshots)
        {
            if (!File.Exists(pair.Key) || File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value))
                continue;
            File.WriteAllBytes(pair.Key, pair.Value);
            AssetDatabase.ImportAsset(pair.Key, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[EvalPlayerBuilder] 构建重新序列化了 {pair.Key}，已还原为构建前的内容");
        }
    }
}
