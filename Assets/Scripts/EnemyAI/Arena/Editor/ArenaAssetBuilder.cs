using System;
using System.Collections.Generic;
using System.IO;
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
///   Assets/ML/Arena/MLArena.unity               场景：镜头、全局光、ArenaManager（复制 8 个训练场）
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
            List<BotPersona> personas = EnsurePersonas(log);

            bool needPlayer = rebuildPlayer || !File.Exists(PlayerPath);
            bool needArena = !File.Exists(ArenaPath);
            bool needScene = !File.Exists(ScenePath);

            // 从 MainGame 读取玩家、基地和全局光（只读；在预览场景中复制，不会弄脏 MainGame）
            GameObject sourcePlayer = null;
            GameObject sourceBase = null;
            Component sourceLight = null;
            if (needPlayer || needArena || needScene)
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

            // 全局光先复制到预览场景：新建场景会关闭 MainGame
            Component lightTemplate = null;
            if (needScene && sourceLight != null)
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

            if (needScene)
                BuildScene(personas, lightTemplate, log);
            else
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            AssetDatabase.SaveAssets();
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

    private static void BuildScene(List<BotPersona> personas, Component lightTemplate, List<string> log)
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
            ("arenaPrefab", AssetDatabase.LoadAssetAtPath<ArenaEnvironment>(ArenaPath)),
            ("cameraController", cameraController));
        var serializedManager = new SerializedObject(manager);
        SerializedProperty list = serializedManager.FindProperty("personas");
        list.arraySize = personas.Count;
        for (int i = 0; i < personas.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = personas[i];
        serializedManager.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        log.Add($"创建 {ScenePath}（ArenaManager：8 个训练场，性格 {personas.Count} 种轮流分配；未加入 Build Settings）");
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
