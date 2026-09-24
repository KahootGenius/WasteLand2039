using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 生存手册页面数据结构
/// </summary>
[Serializable]
public class ManualPage
{
    [Header("页面信息")]
    public string title;
    [TextArea(5, 10)]
    public string content;
    public Sprite illustration; // 可选的插图
    
    public ManualPage(string title, string content, Sprite illustration = null)
    {
        this.title = title;
        this.content = content;
        this.illustration = illustration;
    }
}

/// <summary>
/// 生存手册主题数据结构
/// </summary>
[Serializable]
public class ManualTopic
{
    [Header("主题信息")]
    public string topicName;
    public List<ManualPage> pages = new List<ManualPage>();
    
    public ManualTopic(string topicName)
    {
        this.topicName = topicName;
        this.pages = new List<ManualPage>();
    }
}

/// <summary>
/// 生存手册数据管理器
/// </summary>
[CreateAssetMenu(fileName = "SurvivalManualData", menuName = "Game/Survival Manual Data")]
public class SurvivalManualData : ScriptableObject
{
    [Header("生存手册配置")]
    public List<ManualTopic> topics = new List<ManualTopic>();
    
    private void OnEnable()
    {
        // 如果没有数据，创建默认内容
        if (topics == null || topics.Count == 0)
        {
            InitializeDefaultContent();
        }
    }
    
    /// <summary>
    /// 初始化默认手册内容
    /// </summary>
    private void InitializeDefaultContent()
    {
        topics = new List<ManualTopic>();
        
        // 基础生存主题
        var basicSurvival = new ManualTopic("基础生存");
        basicSurvival.pages.Add(new ManualPage("欢迎来到废土2039", 
            "欢迎来到废土世界！这是一个充满危险但也充满机遇的世界。\n\n" +
            "在这里，你需要收集资源、制作工具、建造庇护所，并与各种威胁作斗争。\n\n" +
            "记住：生存是第一要务，但不要忘记探索这个世界的秘密。"));
        
        basicSurvival.pages.Add(new ManualPage("资源收集", 
            "资源是生存的基础。你可以通过以下方式获得资源：\n\n" +
            "• 探索废弃建筑和宝箱\n" +
            "• 击败敌人获得战利品\n" +
            "• 收集散落在地面的物品\n" +
            "• 与NPC交易获得稀有物品\n\n" +
            "合理管理你的背包空间，优先收集重要资源。"));
        
        basicSurvival.pages.Add(new ManualPage("背包管理", 
            "按Tab键或I键打开背包界面。\n\n" +
            "背包功能：\n" +
            "• 查看和整理物品\n" +
            "• 使用消耗品\n" +
            "• 装备武器和防具\n" +
            "• 丢弃不需要的物品\n\n" +
            "提示：定期清理背包，保持足够的空间收集新物品。"));
        
        topics.Add(basicSurvival);
        
        // 战斗系统主题
        var combatSystem = new ManualTopic("战斗系统");
        combatSystem.pages.Add(new ManualPage("武器使用", 
            "掌握武器使用是生存的关键。\n\n" +
            "基本操作：\n" +
            "• 左键点击或空格键：攻击\n" +
            "• 瞄准敌人进行精确打击\n" +
            "• 注意武器的攻击范围和冷却时间\n\n" +
            "不同武器有不同的特性，选择适合的武器应对不同敌人。"));
        
        combatSystem.pages.Add(new ManualPage("敌人类型", 
            "废土中存在多种敌人：\n\n" +
            "• 普通僵尸：移动缓慢，攻击力中等\n" +
            "• 变异生物：速度较快，具有特殊能力\n" +
            "• 机械单位：防御力高，需要特殊武器\n\n" +
            "了解敌人的弱点，制定相应的战斗策略。"));
        
        combatSystem.pages.Add(new ManualPage("生命值管理", 
            "保持生命值是生存的基础：\n\n" +
            "• 使用医疗包恢复生命值\n" +
            "• 避免不必要的战斗\n" +
            "• 寻找安全的休息点\n" +
            "• 合理使用防御道具\n\n" +
            "记住：预防胜于治疗，避免受伤比治疗更重要。"));
        
        topics.Add(combatSystem);
        
        // 制作系统主题
        var craftingSystem = new ManualTopic("制作系统");
        craftingSystem.pages.Add(new ManualPage("制作基础", 
            "制作系统让你能够创造有用的物品：\n\n" +
            "• 收集制作材料\n" +
            "• 学习制作配方\n" +
            "• 使用制作台制作物品\n" +
            "• 升级制作技能\n\n" +
            "制作的物品通常比拾取的物品更强大。"));
        
        craftingSystem.pages.Add(new ManualPage("重要配方", 
            "以下是一些重要的制作配方：\n\n" +
            "• 基础工具：木棍 + 石头\n" +
            "• 简易武器：金属片 + 布料\n" +
            "• 医疗包：草药 + 绷带\n" +
            "• 食物：生肉 + 火源\n\n" +
            "探索世界可以发现更多高级配方。"));
        
        topics.Add(craftingSystem);
        
        // 探索指南主题
        var explorationGuide = new ManualTopic("探索指南");
        explorationGuide.pages.Add(new ManualPage("地图导航", 
            "熟悉地图是成功探索的关键：\n\n" +
            "• 记住重要地点的位置\n" +
            "• 寻找地标作为导航参考\n" +
            "• 注意危险区域的标记\n" +
            "• 规划安全的探索路线\n\n" +
            "建议先探索安全区域，再逐步深入危险地带。"));
        
        explorationGuide.pages.Add(new ManualPage("宝箱与秘密", 
            "世界中隐藏着许多宝箱和秘密：\n\n" +
            "• 宝箱通常包含珍贵物品\n" +
            "• 注意隐藏的入口和通道\n" +
            "• 某些区域需要特定条件才能进入\n" +
            "• 与NPC对话可能获得线索\n\n" +
            "耐心探索，你会发现意想不到的惊喜。"));
        
        topics.Add(explorationGuide);
        
        Debug.Log("[SurvivalManualData] 已初始化默认手册内容，共 " + topics.Count + " 个主题");
    }
    
    /// <summary>
    /// 获取指定主题的页面数量
    /// </summary>
    public int GetTopicPageCount(int topicIndex)
    {
        if (topicIndex >= 0 && topicIndex < topics.Count)
        {
            return topics[topicIndex].pages.Count;
        }
        return 0;
    }
    
    /// <summary>
    /// 获取指定页面的数据
    /// </summary>
    public ManualPage GetPage(int topicIndex, int pageIndex)
    {
        if (topicIndex >= 0 && topicIndex < topics.Count)
        {
            var topic = topics[topicIndex];
            if (pageIndex >= 0 && pageIndex < topic.pages.Count)
            {
                return topic.pages[pageIndex];
            }
        }
        return null;
    }
}