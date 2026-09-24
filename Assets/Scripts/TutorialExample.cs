using UnityEngine;

/// <summary>
/// 开场指引系统使用示例
/// 演示如何使用基于Light2D的TutorialGuideManager
/// </summary>
public class TutorialExample : MonoBehaviour
{
    [Header("示例设置")]
    [SerializeField] private Transform npcTransform; // NPC位置
    [SerializeField] private KeyCode startTutorialKey = KeyCode.T; // 开始指引的按键
    [SerializeField] private KeyCode endTutorialKey = KeyCode.Y; // 结束指引的按键
    
    private void Start()
    {
        // 如果没有设置NPC位置，创建一个示例位置
        if (npcTransform == null)
        {
            GameObject npcExample = new GameObject("ExampleNPC");
            npcExample.transform.position = new Vector3(2f, 0f, 0f);
            npcTransform = npcExample.transform;
            
            // 添加一个简单的可视化组件
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(npcExample.transform);
            cube.transform.localPosition = Vector3.zero;
            cube.GetComponent<Renderer>().material.color = Color.green;
            
            Debug.Log("[TutorialExample] 已创建示例NPC位置");
        }
        
        // 设置GameManager的指引目标
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetTutorialTarget(npcTransform);
        }
    }
    
    private void Update()
    {
        // 按T键开始指引
        if (Input.GetKeyDown(startTutorialKey))
        {
            StartTutorialDemo();
        }
        
        // 按Y键结束指引
        if (Input.GetKeyDown(endTutorialKey))
        {
            EndTutorialDemo();
        }
        
        // 按空格键切换NPC位置（演示动态更新）
        if (Input.GetKeyDown(KeyCode.Space) && npcTransform != null)
        {
            MoveTutorialTarget();
        }
    }
    
    /// <summary>
    /// 开始指引演示
    /// </summary>
    public void StartTutorialDemo()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartTutorial();
            Debug.Log("[TutorialExample] 开始指引演示 - 按 " + endTutorialKey + " 键结束指引");
        }
        else
        {
            Debug.LogError("[TutorialExample] GameManager实例未找到");
        }
    }
    
    /// <summary>
    /// 结束指引演示
    /// </summary>
    public void EndTutorialDemo()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.EndTutorial();
            Debug.Log("[TutorialExample] 结束指引演示");
        }
    }
    
    /// <summary>
    /// 移动指引目标位置
    /// </summary>
    private void MoveTutorialTarget()
    {
        if (npcTransform != null)
        {
            // 随机移动NPC位置
            Vector3 newPosition = new Vector3(
                Random.Range(-5f, 5f),
                Random.Range(-3f, 3f),
                0f
            );
            
            npcTransform.position = newPosition;
            Debug.Log($"[TutorialExample] NPC移动到新位置: {newPosition}");
        }
    }
    
    /// <summary>
    /// 模拟与NPC对话（结束指引）
    /// </summary>
    public void OnNPCInteraction()
    {
        Debug.Log("[TutorialExample] 玩家与NPC交互，结束指引");
        EndTutorialDemo();
    }
    
    private void OnGUI()
    {
        // 显示操作提示
        GUILayout.BeginArea(new Rect(10, 10, 300, 250));
        GUILayout.Label("开场指引系统演示", GUI.skin.box);
        GUILayout.Label($"按 {startTutorialKey} 键: 开始指引");
        GUILayout.Label($"按 {endTutorialKey} 键: 结束指引");
        GUILayout.Label("按 空格键: 移动NPC位置");
        GUILayout.Label("使用Unity 2D Light系统");
        GUILayout.Label("聚光灯将在3秒后自动消失");
        
        if (GameManager.Instance != null)
        {
            GUILayout.Label($"指引状态: {(GameManager.Instance.IsTutorialActive ? "激活" : "未激活")}");
        }
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("开始指引"))
        {
            StartTutorialDemo();
        }
        
        if (GUILayout.Button("结束指引"))
        {
            EndTutorialDemo();
        }
        
        GUILayout.EndArea();
    }
}