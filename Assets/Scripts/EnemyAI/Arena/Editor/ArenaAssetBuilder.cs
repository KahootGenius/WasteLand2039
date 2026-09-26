using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 生成 ML 训练场所需的资产（Tools > Enemy AI > Build ML Arena）。只创建缺少的资产，已存在的不覆盖：
///   Assets/ML/Arena/Art/ArenaSquare.png        地面 / 路线 / 避难点用的方块精灵
///   Assets/ML/Arena/Prefabs/Zombie_Arena.prefab 001Z 的变体：不掉落物品（其余与游戏中的僵尸相同）
///   Assets/ML/Arena/ArenaWave.asset             复制 Day 3 的尸潮参数，敌人换成 Zombie_Arena
///   Assets/ML/Personas/*.asset                  内置机器人性格（BotPersonaPresets）
///   Assets/ML/Arena/Prefabs/ArenaPlayer.prefab  复制 MainGame 的玩家：去掉拾取、弹药不走背包、加 PlayerBot
///   Assets/ML/Arena/Prefabs/Arena.prefab        一个训练场（基地、三条路线、玩家、生成器 + 指挥官 + 监测）
///   Assets/ML/Arena/MLArena.unity               场景：镜头、全局光、ArenaManager（复制 8 个训练场，固定性格，基准指挥官）
///   Assets/ML/Arena/Prefabs/Arena_RL.prefab     Arena 的变体：指挥官换成 RLCommander（V1 强化学习），每回合 4 波
///   Assets/ML/Arena/MLArena_RL.unity            训练场景：Arena_RL × 8，性格池 + 课程等级 + 打乱路线，不写遥测，
///                                               游戏速度由 ML-Agents 训练器设置
///   Assets/ML/Arena/MLArena_Eval_Baseline.unity 评估：8 个固定性格（含 RunnerB80），每回合 4 波，完整遥测，基准指挥官
///   Assets/ML/Arena/MLArena_Eval_RL.unity       同上，指挥官为 RLCommander（模型见 Install Latest Commander Model）
/// 不会修改 MainGame（只读取玩家、基地精灵和全局光），也不会改动 Build Settings。
/// MainGame 的玩家改动后，用 Tools > Enemy AI > Rebuild Arena Player From MainGame 重新复制。
/// </summary>
public static class ArenaAssetBuilder
{
    private const string Root = "Assets/ML";
    private const string ArenaFolder = Root + "/Arena";
    private const string PrefabFolder = ArenaFolder + "/Prefabs";
    private const string ArtFolder = ArenaFolder + "/Art";
    private const string PersonaFolder = Root + "/Personas";

    private const string SquarePath = ArtFolder + "/ArenaSquare.png";
    private const string ZombiePath = PrefabFolder + "/Zombie_Arena.prefab";
    private const string WavePath = ArenaFolder + "/ArenaWave.asset";
    private const string PlayerPath = PrefabFolder + "/ArenaPlayer.prefab";
    private const string ArenaPath = PrefabFolder + "/Arena.prefab";
    private const string ScenePath = ArenaFolder + "/MLArena.unity";
    private const string ArenaRLPath = PrefabFolder + "/Arena_RL.prefab";
    private const string RLScenePath = ArenaFolder + "/MLArena_RL.unity";
    private const int TrainingWavesPerEpisode = 4;
    private const string EvalBaselineScenePath = ArenaFolder + "/MLArena_Eval_Baseline.unity";
    private const string EvalRLScenePath = ArenaFolder + "/MLArena_Eval_RL.unity";
    private const string ArenaV2Path = PrefabFolder + "/Arena_V2.prefab";
    private const string EvalV2ScenePath = ArenaFolder + "/MLArena_Eval_V2.unity";
    private const string DataScenePath = ArenaFolder + "/MLArena_Data.unity";
    private const string ModelFolder = Root + "/Models";
    private const string SettingsFolder = Root + "/Resources";
    private const string SettingsPath = SettingsFolder + "/" + EnemyAISettings.ResourcePath + ".asset";
    private const string ResultsFolder = "MLTraining/results";
    private static readonly string[] EvalPersonas =
        { "Fighter", "RunnerA", "RunnerB80", "Mixed", "Adaptive", "Kiter", "Random", "RunnerB80" };
    private static readonly string[] EvalOnlyPersonas = { "RunnerB80" };

    private const string MainGamePath = "Assets/MainGame.unity";
    private const string SourceZombiePath = "Assets/Prefabs/001Z.prefab";
    private const string SourceWavePath = "Assets/Day 3.asset";

    // 训练场布局（与 BotTests 的模拟训练场一致）
    private const float RefugeDistance = 26f;
    private const float GroundSize = 84f;
    private static readonly Vector3 PlayerStartPosition = new Vector3(0f, -4.5f, 0f);
    private static readonly (string name, float degrees, Color color)[] Routes =
    {
        ("A", 0f, new Color(0.95f, 0.35f, 0.3f)),
        ("B", 135f, new Color(0.35f, 0.8f, 0.4f)),
        ("C", 225f, new Color(0.35f, 0.55f, 0.95f)),
    };

    [MenuItem("Tools/Enemy AI/Build ML Arena")]
    public static void BuildMenu()
    {
        Build(rebuildPlayer: false);
    }

    [MenuItem("Tools/Enemy AI/Rebuild Arena Player From MainGame")]
    public static void RebuildPlayerMenu()
    {
        Build(rebuildPlayer: true);
    }

    private static void Build(bool rebuildPlayer)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var log = new List<string>();
        Scene preview = EditorSceneManager.NewPreviewScene();
        Scene mainGame = default;
        bool openedMainGame = false;
        try
        {
            EnsureFolder(Root);
            EnsureFolder(ArenaFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(ArtFolder);
            EnsureFolder(PersonaFolder);

            Sprite square = EnsureSquareSprite(log);
            GameObject zombie = EnsureZombieVariant(preview, log);
            HordeEvent wave = EnsureWave(zombie, log);
            // 只保存路径：新建场景会卸载未被引用的资产，之前取得的对象会失效
            List<string> personas = EnsurePersonas(log).Select(p => AssetDatabase.GetAssetPath(p)).ToList();

            bool needPlayer = rebuildPlayer || !File.Exists(PlayerPath);
            bool needArena = !File.Exists(ArenaPath);
            bool needScene = !File.Exists(ScenePath);
            bool needRLScene = !File.Exists(RLScenePath);
            bool needEvalScenes = !File.Exists(EvalBaselineScenePath) || !File.Exists(EvalRLScenePath) || !File.Exists(EvalV2ScenePath);
            bool needDataScene = !File.Exists(DataScenePath);

            // 从 MainGame 读取玩家、基地和全局光（只读；在预览场景中复制，不会弄脏 MainGame）
            GameObject sourcePlayer = null;
            GameObject sourceBase = null;
            Component sourceLight = null;
            if (needPlayer || needArena || needScene || needRLScene || needEvalScenes || needDataScene)
            {
                mainGame = SceneManager.GetSceneByPath(MainGamePath);
                if (!mainGame.isLoaded)
                {
                    mainGame = EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Additive);
                    openedMainGame = true;
                }
                FindMainGameSources(mainGame, out sourcePlayer, out sourceBase, out sourceLight);
                if (sourcePlayer == null || sourceBase == null)
                    throw new InvalidOperationException("MainGame 中找不到 PlayerController 或 MainBase");
            }

            if (needPlayer)
                BuildArenaPlayer(sourcePlayer, preview, log);
            if (needArena)
                BuildArenaPrefab(sourceBase, square, wave, preview, log);
            EnsureRLArenaVariant(preview, log);
            EnsureV2ArenaVariant(preview, log);

            // 全局光先复制到预览场景：新建场景会关闭 MainGame
            Component lightTemplate = null;
            if ((needScene || needRLScene || needEvalScenes || needDataScene) && sourceLight != null)
            {
                var holder = new GameObject("Light Template");
                SceneManager.MoveGameObjectToScene(holder, preview);
                lightTemplate = CopyComponent(sourceLight, holder);
            }

            if (openedMainGame)
            {
                EditorSceneManager.CloseScene(mainGame, true);
                openedMainGame = false;
            }

            if (needRLScene)
                BuildScene(RLScenePath, ArenaRLPath, null, TrainingPool(personas), lightTemplate, log);
            if (needEvalScenes)
            {
                List<string> evalPersonas = EvalPersonas.Select(n => personas.First(p => Path.GetFileNameWithoutExtension(p) == n)).ToList();
                if (!File.Exists(EvalBaselineScenePath))
                    BuildScene(EvalBaselineScenePath, ArenaPath, evalPersonas, null, lightTemplate, log, evaluation: true);
                if (!File.Exists(EvalRLScenePath))
                    BuildScene(EvalRLScenePath, ArenaRLPath, evalPersonas, null, lightTemplate, log, evaluation: true);
                if (!File.Exists(EvalV2ScenePath))
                    BuildScene(EvalV2ScenePath, ArenaV2Path, evalPersonas, null, lightTemplate, log, evaluation: true);
            }
            if (needDataScene)
                BuildScene(DataScenePath, ArenaPath, null, TrainingPool(personas), lightTemplate, log, dataCollection: true);
            if (needScene)
                BuildScene(ScenePath, ArenaPath, personas, null, lightTemplate, log);
            else
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 不调用 AssetDatabase.SaveAssets()：本工具创建的资产都已单独保存；全项目保存会把运行时被游戏弄脏的资产
            // （MainGame 会移动 Weapon.prefab 资产的 Firepoint，见 IDEAS.md）也写入磁盘
            Debug.Log("[ArenaAssetBuilder] 完成。" + (log.Count > 0 ? "\n" + string.Join("\n", log) : "\n所有资产已存在，未作修改"));
        }
        catch (Exception exception)
        {
            Debug.LogError("[ArenaAssetBuilder] 失败: " + exception);
        }
        finally
        {
            if (openedMainGame && mainGame.isLoaded)
                EditorSceneManager.CloseScene(mainGame, true);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    // ---------- 数据资产 ----------

    private static Sprite EnsureSquareSprite(List<string> log)
    {
        if (!File.Exists(SquarePath))
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color32[64];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(SquarePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(SquarePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(SquarePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 8f; // 1 × 1 单位
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            log.Add("创建 " + SquarePath);
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
    }

    private static GameObject EnsureZombieVariant(Scene preview, List<string> log)
    {
        if (!File.Exists(ZombiePath))
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceZombiePath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
            var enemy = new SerializedObject(instance.GetComponent<Enemy>());
            enemy.FindProperty("dropChance").floatValue = 0f;
            enemy.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(instance, ZombiePath);
            Object.DestroyImmediate(instance);
            log.Add("创建 " + ZombiePath + "（001Z 的变体，dropChance = 0）");
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(ZombiePath);
    }

    private static HordeEvent EnsureWave(GameObject zombie, List<string> log)
    {
        var wave = AssetDatabase.LoadAssetAtPath<HordeEvent>(WavePath);
        if (wave != null)
            return wave;

        wave = ScriptableObject.CreateInstance<HordeEvent>();
        var source = AssetDatabase.LoadAssetAtPath<HordeEvent>(SourceWavePath);
        if (source != null)
            EditorUtility.CopySerialized(source, wave);
        wave.name = "ArenaWave";
        wave.hordeName = "ML Arena Wave";
        wave.description = "ML 训练场的尸潮：参数复制自 Day 3，敌人为不掉落物品的 Zombie_Arena";
        wave.enemyTypes = new[] { new EnemyTypeWeight { enemyPrefab = zombie, weight = 1f } };
        AssetDatabase.CreateAsset(wave, WavePath);
        log.Add($"创建 {WavePath}（{wave.totalEnemyCount} 个敌人，最多同时 {wave.maxActiveEnemies} 个，间隔 {wave.spawnInterval}s，半径 {wave.minSpawnDistance}–{wave.spawnRadius}）");
        return wave;
    }

    private static List<BotPersona> EnsurePersonas(List<string> log)
    {
        var personas = new List<BotPersona>();
        foreach (BotPersonaPresets.Preset preset in BotPersonaPresets.All)
        {
            string path = $"{PersonaFolder}/{preset.Name}.asset";
            var persona = AssetDatabase.LoadAssetAtPath<BotPersona>(path);
            if (persona == null)
            {
                persona = ScriptableObject.CreateInstance<BotPersona>();
                persona.description = preset.Description;
                persona.spec = preset.Spec;
                AssetDatabase.CreateAsset(persona, path);
                log.Add("创建 " + path);
            }
            personas.Add(persona);
        }
        return personas;
    }

    // ---------- 从 MainGame 读取 ----------

    private static void FindMainGameSources(Scene scene, out GameObject player, out GameObject mainBase, out Component globalLight)
    {
        player = null;
        mainBase = null;
        globalLight = null;
        Type lightType = Type.GetType("UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (player == null)
            {
                var controller = root.GetComponentInChildren<PlayerController>(true);
                if (controller != null)
                    player = controller.gameObject;
            }
            if (mainBase == null)
            {
                var baseComponent = root.GetComponentInChildren<MainBase>(true);
                if (baseComponent != null)
                    mainBase = baseComponent.gameObject;
            }
            if (globalLight == null && lightType != null)
            {
                foreach (Component light in root.GetComponentsInChildren(lightType, true))
                {
                    if (new SerializedObject(light).FindProperty("m_LightType").intValue == 4) // Global
                    {
                        globalLight = light;
                        break;
                    }
                }
            }
        }
    }

    // ---------- 预制体 ----------

    private static void BuildArenaPlayer(GameObject source, Scene preview, List<string> log)
    {
        var holder = new GameObject("Holder");
        SceneManager.MoveGameObjectToScene(holder, preview);
        GameObject player = Object.Instantiate(source, holder.transform);
        player.name = "ArenaPlayer";
        player.transform.localPosition = Vector3.zero;

        RemapWeaponsToChildren(player, log);

        // 训练场没有背包：去掉拾取，弹药不从背包扣
        foreach (var collector in player.GetComponentsInChildren<PlayerItemCollector>(true))
            Object.DestroyImmediate(collector);
        foreach (var weapon in player.GetComponentsInChildren<RangedWeapon>(true))
        {
            var serialized = new SerializedObject(weapon);
            serialized.FindProperty("requireAmmoFromInventory").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 死亡动画的事件会打开 DiePanel：训练场用一个空物体代替游戏的 UI
        var diePanel = new GameObject("DiePanel (arena placeholder)");
        diePanel.transform.SetParent(player.transform, false);
        diePanel.SetActive(false);
        player.GetComponent<PlayerController>().DiePanel = diePanel;

        if (player.GetComponent<PlayerBot>() == null)
            player.AddComponent<PlayerBot>();

        PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        Object.DestroyImmediate(holder);
        log.Add($"创建 {PlayerPath}（复制自 MainGame 的 {source.name}：去掉 PlayerItemCollector，弹药不走背包，加 PlayerBot）");
    }

    /// <summary>
    /// MainGame 玩家的 WeaponManager.availableWeapons 引用的是预制体资产 Assets/Prefabs/Weapon.prefab，
    /// 而不是玩家子物体上的武器。多个训练场会因此共用同一把武器（射击冷却、弹药、枪口位置全部共享），
    /// 运行时还会改动资产本身。训练场的玩家改为使用自己的子物体武器（同名优先；战斗数值与资产相同）。
    /// </summary>
    private static void RemapWeaponsToChildren(GameObject player, List<string> log)
    {
        var manager = player.GetComponentInChildren<WeaponManager>(true);
        if (manager == null)
            return;

        RangedWeapon[] children = player.GetComponentsInChildren<RangedWeapon>(true);
        var serialized = new SerializedObject(manager);
        SerializedProperty list = serialized.FindProperty("availableWeapons");
        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            var weapon = element.objectReferenceValue as RangedWeapon;
            if (weapon == null || !EditorUtility.IsPersistent(weapon))
                continue; // 已经是玩家自己的武器

            RangedWeapon child = Array.Find(children, c => c.name == weapon.name);
            if (child == null && children.Length > 0)
                child = children[0];
            if (child == null)
                throw new InvalidOperationException($"availableWeapons[{i}] 引用预制体资产 {AssetDatabase.GetAssetPath(weapon)}，但玩家没有子物体武器可以替代");

            element.objectReferenceValue = child;
            log.Add($"  availableWeapons[{i}]: 预制体资产 {AssetDatabase.GetAssetPath(weapon)} → 玩家子物体 {child.name}");
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildArenaPrefab(GameObject sourceBase, Sprite square, HordeEvent wave, Scene preview, List<string> log)
    {
        var root = new GameObject("Arena");
        SceneManager.MoveGameObjectToScene(root, preview);
        var arena = root.AddComponent<ArenaEnvironment>();

        CreateSprite("Ground", root.transform, square, new Color(0.22f, 0.24f, 0.2f), Vector3.zero, 0f,
            new Vector3(GroundSize, GroundSize, 1f), -100);

        // 基地：与 MainGame 的基地相同的精灵和碰撞体（敌人在基地周围的行为一致），用 ArenaBase 代替 MainBase
        var baseObject = new GameObject("Base");
        baseObject.transform.SetParent(root.transform, false);
        baseObject.transform.localScale = sourceBase.transform.localScale;
        var sourceRenderer = sourceBase.GetComponent<SpriteRenderer>();
        if (sourceRenderer != null)
        {
            CopyComponent(sourceRenderer, baseObject);
        }
        var sourceCollider = sourceBase.GetComponent<BoxCollider2D>();
        if (sourceCollider != null)
        {
            CopyComponent(sourceCollider, baseObject);
        }
        baseObject.layer = sourceBase.layer;
        var arenaBase = baseObject.AddComponent<ArenaBase>();

        var routeComponents = new List<ArenaRoute>();
        foreach (var (routeName, degrees, color) in Routes)
        {
            float rad = degrees * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            CreateSprite("Lane " + routeName, root.transform, square, new Color(color.r, color.g, color.b, 0.18f),
                direction * RefugeDistance * 0.5f, degrees, new Vector3(RefugeDistance, 1.2f, 1f), -95);

            GameObject refuge = CreateSprite("Refuge " + routeName, root.transform, square, new Color(color.r, color.g, color.b, 0.35f),
                direction * RefugeDistance, 0f, new Vector3(5f, 5f, 1f), -90);
            var route = refuge.AddComponent<ArenaRoute>();
            var serializedRoute = new SerializedObject(route);
            serializedRoute.FindProperty("routeName").stringValue = routeName;
            serializedRoute.FindProperty("gizmoColor").colorValue = color;
            serializedRoute.ApplyModifiedPropertiesWithoutUndo();
            routeComponents.Add(route);
        }

        var playerStart = new GameObject("PlayerStart");
        playerStart.transform.SetParent(root.transform, false);
        playerStart.transform.localPosition = PlayerStartPosition;

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, root.transform);
        player.transform.localPosition = PlayerStartPosition;

        var director = new GameObject("Director");
        director.transform.SetParent(root.transform, false);
        var commander = director.AddComponent<BaselineCommander>();
        var spawner = director.AddComponent<HordeEventSpawner>();
        spawner.playerTransform = player.transform;
        spawner.mainBaseTransform = baseObject.transform;
        spawner.autoStart = false;
        spawner.endlessWaves = false;
        spawner.bindEnemiesToSpawner = true;
        SetProperties(spawner, ("commanderComponent", commander));

        var monitor = director.AddComponent<PlayerBehaviourMonitor>();
        SetProperties(monitor,
            ("spawner", spawner),
            ("player", player.GetComponent<PlayerController>()),
            ("weaponManager", player.GetComponentInChildren<WeaponManager>()),
            ("mainBaseOverride", baseObject.transform));
        var serializedMonitor = new SerializedObject(monitor);
        serializedMonitor.FindProperty("trackSpawnerEnemiesOnly").boolValue = true;
        serializedMonitor.FindProperty("overlayKey").intValue = (int)KeyCode.None; // ArenaManager 统一处理 ` 键
        serializedMonitor.ApplyModifiedPropertiesWithoutUndo();

        SetProperties(arena,
            ("spawner", spawner),
            ("monitor", monitor),
            ("player", player.GetComponent<PlayerController>()),
            ("bot", player.GetComponent<PlayerBot>()),
            ("arenaBase", arenaBase),
            ("playerStart", playerStart.transform),
            ("wave", wave));
        var serializedArena = new SerializedObject(arena);
        SerializedProperty routes = serializedArena.FindProperty("routes");
        routes.arraySize = routeComponents.Count;
        for (int i = 0; i < routeComponents.Count; i++)
            routes.GetArrayElementAtIndex(i).objectReferenceValue = routeComponents[i];
        serializedArena.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, ArenaPath);
        Object.DestroyImmediate(root);
        log.Add($"创建 {ArenaPath}（基地 + 路线 A 东 / B 西北 / C 西南，避难点距基地 {RefugeDistance}）");
    }

    private static GameObject CreateSprite(string name, Transform parent, Sprite sprite, Color color, Vector3 position,
        float degrees, Vector3 scale, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, degrees);
        go.transform.localScale = scale;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        return go;
    }

    // ---------- 场景 ----------

    /// <summary>
    /// 训练用性格池（课程）：0 = 只有逃跑型（路线每回合打乱）；1 = 加入混合 / 风筝 / 作战型；2 = 加入适应型和随机型
    /// </summary>
    private static List<(string path, int minLevel)> TrainingPool(List<string> personas)
    {
        var levels = new Dictionary<string, int>
        {
            { "RunnerA", 0 }, { "Mixed", 1 }, { "Kiter", 1 }, { "Fighter", 1 }, { "Adaptive", 2 }, { "Random", 2 }
        };
        var pool = new List<(string path, int minLevel)>();
        foreach (string path in personas)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!EvalOnlyPersonas.Contains(name))
                pool.Add((path, levels.TryGetValue(name, out int level) ? level : 2));
        }
        return pool;
    }

    private static void EnsureRLArenaVariant(Scene preview, List<string> log)
    {
        if (File.Exists(ArenaRLPath))
            return;

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
        GameObject director = instance.GetComponentInChildren<HordeEventSpawner>(true).gameObject;

        var commander = director.AddComponent<RLCommander>(); // RequireComponent 会一并添加 BehaviorParameters
        RLCommander.ConfigureBehavior(director.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>());
        SetProperties(director.GetComponent<HordeEventSpawner>(), ("commanderComponent", commander));

        var arena = new SerializedObject(instance.GetComponent<ArenaEnvironment>());
        arena.FindProperty("wavesPerEpisode").intValue = TrainingWavesPerEpisode;
        arena.ApplyModifiedPropertiesWithoutUndo();

        instance.name = "Arena_RL";
        PrefabUtility.SaveAsPrefabAsset(instance, ArenaRLPath);
        Object.DestroyImmediate(instance);
        log.Add($"创建 {ArenaRLPath}（Arena 的变体：指挥官 = RLCommander，行为名 {RLCommander.DefaultBehaviorName}，" +
                $"观察 {RLCommander.ObservationSize}，动作 [{string.Join(", ", CommanderActions.BranchSizes(RLCommander.ExpectedZoneCount))}]，每回合 {TrainingWavesPerEpisode} 波）");
    }

    /// <summary>
    /// V2 训练场：Arena 的变体，指挥官 = PredictiveCommander（默认统计预测器），每回合 4 波（与评估一致）
    /// </summary>
    private static void EnsureV2ArenaVariant(Scene preview, List<string> log)
    {
        if (File.Exists(ArenaV2Path))
            return;

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
        GameObject director = instance.GetComponentInChildren<HordeEventSpawner>(true).gameObject;

        var commander = director.AddComponent<PredictiveCommander>();
        SetProperties(director.GetComponent<HordeEventSpawner>(), ("commanderComponent", commander));

        var arena = new SerializedObject(instance.GetComponent<ArenaEnvironment>());
        arena.FindProperty("wavesPerEpisode").intValue = TrainingWavesPerEpisode;
        arena.ApplyModifiedPropertiesWithoutUndo();

        instance.name = "Arena_V2";
        PrefabUtility.SaveAsPrefabAsset(instance, ArenaV2Path);
        Object.DestroyImmediate(instance);
        log.Add($"创建 {ArenaV2Path}（Arena 的变体：指挥官 = PredictiveCommander（统计预测器），每回合 {TrainingWavesPerEpisode} 波）");
    }

    /// <param name="evaluation">评估场景：固定性格、4 波、锁步 0.02 秒、完整遥测</param>
    /// <param name="dataCollection">数据场景（V2 预测器的训练数据）：性格池（全部等级）、打乱路线、基准指挥官、4 波、锁步、只写事件</param>
    private static void BuildScene(string scenePath, string arenaPrefabPath, List<string> personas,
        List<(string path, int minLevel)> pool, Component lightTemplate, List<string> log, bool evaluation = false,
        bool dataCollection = false)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.09f, 0.08f);
        cameraObject.AddComponent<AudioListener>();
        var cameraController = cameraObject.AddComponent<CameraController>();

        if (lightTemplate != null)
        {
            var lightObject = new GameObject("Global Light 2D");
            Component light = CopyComponent(lightTemplate, lightObject);
            var serializedLight = new SerializedObject(light);
            serializedLight.FindProperty("m_Intensity").floatValue = 1f;
            serializedLight.FindProperty("m_Color").colorValue = Color.white;
            serializedLight.ApplyModifiedPropertiesWithoutUndo();
        }

        var managerObject = new GameObject("ArenaManager");
        var manager = managerObject.AddComponent<ArenaManager>();
        SetProperties(manager,
            ("arenaPrefab", AssetDatabase.LoadAssetAtPath<ArenaEnvironment>(arenaPrefabPath)),
            ("cameraController", cameraController));
        var serializedManager = new SerializedObject(manager);
        if (personas != null)
        {
            SerializedProperty list = serializedManager.FindProperty("personas");
            list.arraySize = personas.Count;
            for (int i = 0; i < personas.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<BotPersona>(personas[i]);
        }
        if (pool != null)
        {
            SerializedProperty list = serializedManager.FindProperty("personaPool");
            list.arraySize = pool.Count;
            for (int i = 0; i < pool.Count; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("persona").objectReferenceValue = AssetDatabase.LoadAssetAtPath<BotPersona>(pool[i].path);
                entry.FindPropertyRelative("minLevel").intValue = pool[i].minLevel;
            }
            // 训练：打乱路线、不写遥测（省磁盘）、游戏速度交给训练器（--time-scale）
            serializedManager.FindProperty("shuffleRoutes").boolValue = true;
            serializedManager.FindProperty("telemetry").enumValueIndex = (int)ArenaTelemetry.Off;
            serializedManager.FindProperty("timeScale").floatValue = 0f;
        }
        if (dataCollection)
        {
            // 数据收集：打乱路线（和训练一样，预测器必须读画像才能猜对），只写事件（含 escape_start 的特征向量），锁步运行
            serializedManager.FindProperty("wavesPerEpisode").intValue = TrainingWavesPerEpisode;
            serializedManager.FindProperty("timeScale").floatValue = 8f;
            serializedManager.FindProperty("lockstepFrameTime").floatValue = 0.02f;
            serializedManager.FindProperty("telemetry").enumValueIndex = (int)ArenaTelemetry.EventsOnly;
        }
        if (evaluation)
        {
            // 评估：两种指挥官使用相同的性格、种子和回合结构（4 波），完整遥测
            serializedManager.FindProperty("wavesPerEpisode").intValue = TrainingWavesPerEpisode;
            serializedManager.FindProperty("timeScale").floatValue = 8f;
            serializedManager.FindProperty("lockstepFrameTime").floatValue = 0.02f;
            serializedManager.FindProperty("telemetry").enumValueIndex = (int)ArenaTelemetry.Full;
        }
        serializedManager.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, scenePath);
        log.Add(dataCollection ? $"创建 {scenePath}（数据收集：基准指挥官，性格池 {pool.Count} 种（全部等级），打乱路线，每回合 {TrainingWavesPerEpisode} 波，只写事件）" :
            evaluation ? $"创建 {scenePath}（评估：{string.Join(", ", personas.Select(Path.GetFileNameWithoutExtension))}；每回合 {TrainingWavesPerEpisode} 波，完整遥测）" :
            pool != null
            ? $"创建 {scenePath}（训练：Arena_RL × 8，性格池 {pool.Count} 种按课程等级抽取，打乱路线，不写遥测；未加入 Build Settings）"
            : $"创建 {scenePath}（ArenaManager：8 个训练场，性格 {personas.Count} 种轮流分配；未加入 Build Settings）");
    }

    // ---------- 训练好的模型 ----------

    /// <summary>
    /// 把最新一次训练（MLTraining/results/*/HordeCommander.onnx，按修改时间，忽略 smoke*）复制到
    /// Assets/ML/Models/HordeCommander_{运行名}.onnx，并设为 Arena_RL 的模型（确定性推理）。
    /// 没有连接训练器时 RLCommander 使用此模型；训练时忽略
    /// </summary>
    [MenuItem("Tools/Enemy AI/Install Latest Commander Model")]
    public static void InstallLatestModel()
    {
        string latest = Directory.Exists(ResultsFolder)
            ? Directory.GetDirectories(ResultsFolder)
                .Where(d => !Path.GetFileName(d).StartsWith("smoke") && File.Exists(Path.Combine(d, "HordeCommander.onnx")))
                .OrderByDescending(d => File.GetLastWriteTime(Path.Combine(d, "HordeCommander.onnx")))
                .FirstOrDefault()
            : null;
        if (latest == null)
        {
            Debug.LogError($"[ArenaAssetBuilder] {ResultsFolder} 下没有训练结果（HordeCommander.onnx）");
            return;
        }
        InstallModel(Path.Combine(latest, "HordeCommander.onnx"), Path.GetFileName(latest));
    }

    public static void InstallModel(string onnxPath, string runId)
    {
        EnsureFolder(Root);
        EnsureFolder(ModelFolder);
        string target = $"{ModelFolder}/HordeCommander_{runId}.onnx";
        File.Copy(onnxPath, target, true);
        AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
        var model = AssetDatabase.LoadAssetAtPath<Unity.Sentis.ModelAsset>(target);
        if (model == null)
        {
            Debug.LogError($"[ArenaAssetBuilder] 无法导入模型 {target}");
            return;
        }

        CommanderActions.Scheme? scheme = RLCommander.DetectScheme(model);
        if (scheme == null)
        {
            Debug.LogError($"[ArenaAssetBuilder] 无法识别模型 {target} 的动作方案（action_masks 大小不符）");
            return;
        }
        ConfigureRLArena(scheme.Value, model);

        EnemyAISettings settings = EnsureSettings();
        settings.rlModel = model;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log($"[ArenaAssetBuilder] 已安装模型 {target} → {ArenaRLPath}（确定性推理）和 {SettingsPath}" +
                  $"（游戏场景当前使用：{settings.gameCommander}）");
    }

    /// <summary>
    /// 设置 Arena_RL 的动作方案（BehaviorParameters 的动作分支 + RLCommander）和模型。
    /// 训练新方案前也要调用（model 为 null），然后重新构建训练用的玩家程序
    /// </summary>
    public static void ConfigureRLArena(CommanderActions.Scheme scheme, Unity.Sentis.ModelAsset model)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ArenaRLPath);
        try
        {
            var behavior = root.GetComponentInChildren<Unity.MLAgents.Policies.BehaviorParameters>(true);
            RLCommander.ConfigureBehavior(behavior, scheme);
            behavior.Model = model;
            behavior.DeterministicInference = true;
            var commander = root.GetComponentInChildren<RLCommander>(true);
            commander.ActionScheme = scheme;
            commander.DecisionInterval = RLCommander.TrainedDecisionInterval(scheme);
            PrefabUtility.SaveAsPrefabAsset(root, ArenaRLPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        Debug.Log($"[ArenaAssetBuilder] {ArenaRLPath}：动作方案 {scheme}（决策间隔 {RLCommander.TrainedDecisionInterval(scheme)} 秒），动作分支 " +
                  $"[{string.Join(", ", CommanderActions.BranchSizes(RLCommander.ExpectedZoneCount, scheme))}]，模型 {(model != null ? model.name : "无")}");
    }

    // ---------- 游戏场景使用的指挥官 ----------

    private const string MenuBaseline = "Tools/Enemy AI/Game Commander: Baseline";
    private const string MenuRL = "Tools/Enemy AI/Game Commander: RL";
    private const string MenuV2 = "Tools/Enemy AI/Game Commander: V2 (Predictive)";

    [MenuItem(MenuBaseline, false, 100)]
    private static void UseBaselineInGame()
    {
        SetGameCommander(EnemyAISettings.CommanderChoice.Baseline);
    }

    [MenuItem(MenuRL, false, 101)]
    private static void UseRLInGame()
    {
        SetGameCommander(EnemyAISettings.CommanderChoice.RL);
    }

    [MenuItem(MenuV2, false, 102)]
    private static void UseV2InGame()
    {
        SetGameCommander(EnemyAISettings.CommanderChoice.Predictive);
    }

    [MenuItem(MenuBaseline, true)]
    [MenuItem(MenuRL, true)]
    [MenuItem(MenuV2, true)]
    private static bool ValidateGameCommanderMenu()
    {
        EnemyAISettings settings = AssetDatabase.LoadAssetAtPath<EnemyAISettings>(SettingsPath);
        var choice = settings != null ? settings.gameCommander : EnemyAISettings.CommanderChoice.Baseline;
        Menu.SetChecked(MenuBaseline, choice == EnemyAISettings.CommanderChoice.Baseline);
        Menu.SetChecked(MenuRL, choice == EnemyAISettings.CommanderChoice.RL);
        Menu.SetChecked(MenuV2, choice == EnemyAISettings.CommanderChoice.Predictive);
        return true;
    }

    private static void SetGameCommander(EnemyAISettings.CommanderChoice choice)
    {
        EnemyAISettings settings = EnsureSettings();
        settings.gameCommander = choice;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        if (choice == EnemyAISettings.CommanderChoice.RL && settings.rlModel == null)
            Debug.LogWarning("[ArenaAssetBuilder] 游戏场景将使用 RL 指挥官，但还没有模型（Tools > Enemy AI > Install Latest Commander Model）；在此之前仍使用基准指挥官");
        else
            Debug.Log($"[ArenaAssetBuilder] 游戏场景（MainGame 等）使用的指挥官：{choice}");
    }

    private static EnemyAISettings EnsureSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<EnemyAISettings>(SettingsPath);
        if (settings != null)
            return settings;
        EnsureFolder(Root);
        EnsureFolder(SettingsFolder);
        settings = ScriptableObject.CreateInstance<EnemyAISettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
        return settings;
    }

    // ---------- 工具 ----------

    /// <summary>复制组件到目标物体（等同于 Inspector 的 Copy Component / Paste Component As New）</summary>
    private static Component CopyComponent(Component source, GameObject target)
    {
        if (!UnityEditorInternal.ComponentUtility.CopyComponent(source) ||
            !UnityEditorInternal.ComponentUtility.PasteComponentAsNew(target))
            throw new InvalidOperationException($"无法复制组件 {source.GetType().Name}");
        Component[] components = target.GetComponents(source.GetType());
        return components[components.Length - 1];
    }

    private static void SetProperties(Object target, params (string name, Object value)[] properties)
    {
        var serialized = new SerializedObject(target);
        foreach (var (name, value) in properties)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
                throw new InvalidOperationException($"{target.GetType().Name} 没有序列化字段 {name}");
            property.objectReferenceValue = value;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
