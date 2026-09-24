using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;

[System.Serializable]
public class DialogueLine
{
    public string speaker;
    public Sprite speakerImage;
    [TextArea(3, 10)]
    public string text;
}

public class DialogueSystem : MonoBehaviour
{
    [Header("UI组件")]
    public Image speakerImage;
    public Text speakerText;
    public Text dialogueText;
    public GameObject dialoguePanel;
    public GameObject continueIndicator;
    
    [Header("对话内容")]
    public DialogueLine[] dialogueLines;
    
    [Header("任务设置")]
    [SerializeField] private string dialogueId = "";
    [SerializeField] private bool isQuestDialogue = false;
    [SerializeField] private bool enableEnemySpawningAfterComplete = false;
    [Header("一次性对话设置")]
    [Tooltip("勾选后此对话只能触发一次")]
    [SerializeField] private bool isOneTimeDialogue = false;
    [Tooltip("使用全局ID（不依赖NPC），适用于场景中的固定对话")]
    [SerializeField] private bool useGlobalId = false;
    

    
    [Header("控制设置")]
    public float typingSpeed = 0.05f;
    public KeyCode continueKey = KeyCode.Space;
    public KeyCode startKey = KeyCode.D;
    public bool isEnter;
    public GameTimer dayManager;
    
    [Header("天数控制")]
    [Tooltip("对话完成后是否自动进入下一天")]
    public bool advanceDayOnComplete = false;
    
    [Header("玩家控制设置")]
    [SerializeField] private bool disablePlayerControlDuringDialogue = true;
    [SerializeField] private bool disableWeaponsDuringDialogue = true;
     public bool hasBeenTriggered = false;
     public GameObject NextNPC;
    // 私有变量
    private int currentLineIndex;
    private bool isTyping;
    private bool isDialogueActive;
    private string currentLine;
    private Coroutine typingCoroutine;
   
    
    // 玩家控制引用
    private PlayerController playerController;
    private WeaponManager weaponManager;
    
    // 事件系统
    public static event System.Action<string> OnDialogueStarted;
    public static event System.Action<string> OnDialogueCompleted;
  
    private string highlightTag = "<color=red>{0}</color>";

    void Start()
    {
        // 初始化对话系统（只隐藏UI，不触发事件）
        dialoguePanel.SetActive(false);
        isDialogueActive = false;
        
        // 获取玩家控制器和武器管理器引用
        FindPlayerComponents();
        
        
        // 检查是否已经完成过此对话
    
    }
    


    
    /// <summary>
    /// 初始化NPC引用
    /// </summary>
    
    /// <summary>
    /// 查找玩家相关组件
    /// </summary>
    private void FindPlayerComponents()
    {
        // 查找玩家控制器
        if (playerController == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerController = player.GetComponent<PlayerController>();
            }
            
            if (playerController == null)
            {
                playerController = FindObjectOfType<PlayerController>();
            }
        }
        
        // 查找武器管理器
        if (weaponManager == null && playerController != null)
        {
            weaponManager = playerController.GetComponentInChildren<WeaponManager>();
            
            if (weaponManager == null)
            {
                weaponManager = FindObjectOfType<WeaponManager>();
            }
        }
        
        if (playerController == null)
        {
            Debug.LogWarning("[DialogueSystem] 未找到PlayerController组件");
        }
        
        if (weaponManager == null)
        {
            Debug.LogWarning("[DialogueSystem] 未找到WeaponManager组件");
        }
    }
    
    void Update()
    {

        if(hasBeenTriggered==false){
 if (Input.GetKeyDown(startKey) && !isDialogueActive && isEnter==true)
        {
            StartDialogue();
        }
        
      
        if (Input.GetKeyDown(continueKey) && isDialogueActive)
        {
            OnContinue();
        }
        
      
        if (continueIndicator != null)
        {
            continueIndicator.SetActive(!isTyping && isDialogueActive && currentLineIndex < dialogueLines.Length);
        }
        }
        
       
    }
    
    public void StartDialogue()
    {

        
        currentLineIndex = 0;
        isDialogueActive = true;
        ShowDialogue();
        DisplayCurrentLine();
        
        // 触发对话开始事件
        OnDialogueStarted?.Invoke(dialogueId);
        
        if (!string.IsNullOrEmpty(dialogueId))
        {
            Debug.Log($"[DialogueSystem] 开始对话: {dialogueId}");
        }
    }
    
    
    

    public void StartDialogue(DialogueLine[] lines)
    {
        dialogueLines = lines;
        StartDialogue();
    }
    
    void DisplayCurrentLine()
    {
        if (currentLineIndex >= dialogueLines.Length)
        {
            // 根据开关决定是否进入下一天
            if (advanceDayOnComplete && dayManager != null)
            {
                dayManager.AdvanceToNextDay();
            }
            
            hasBeenTriggered=true;
            HideDialogue();
            return;
        }
        
        DialogueLine line = dialogueLines[currentLineIndex];
        speakerText.text = line.speaker;
        
      
        if (line.speakerImage != null)
        {
            speakerImage.sprite = line.speakerImage;
            speakerImage.gameObject.SetActive(true);
        }
        else
        {
            speakerImage.gameObject.SetActive(false);
        }
        
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }
        
       
        string processedText = ProcessHighlightedText(line.text);
        typingCoroutine = StartCoroutine(TypeText(processedText));
        currentLineIndex++;
    }
    
    string ProcessHighlightedText(string text)
    {
   
        return Regex.Replace(text, @"\{([^{}]*)\}", 
            match => string.Format(highlightTag, match.Groups[1].Value));
    }
    
    IEnumerator TypeText(string text)
    {
        isTyping = true;
        dialogueText.text = "";
        currentLine = text;
        
     
        foreach (char letter in text.ToCharArray())
        {
            dialogueText.text += letter;
            yield return new WaitForSeconds(typingSpeed);
        }
        
        isTyping = false;
    }
    
    void OnContinue()
    {
        if (isTyping)
        {
            
            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
            }
            dialogueText.text = currentLine;
            isTyping = false;
        }
        else
        {
            DisplayCurrentLine();
        }
    }
    
    void ShowDialogue()
    {
        dialoguePanel.SetActive(true);
        
        // 禁用玩家控制
        if (disablePlayerControlDuringDialogue && playerController != null)
        {
            playerController.DisableControl();
            Debug.Log("[DialogueSystem] 对话开始，已禁用玩家移动控制");
        }
        
        // 禁用武器系统
        if (disableWeaponsDuringDialogue && weaponManager != null)
        {
            weaponManager.enabled = false;
            Debug.Log("[DialogueSystem] 对话开始，已禁用武器系统");
        }
    }
    
    void HideDialogue()
    {
        dialoguePanel.SetActive(false);
        isDialogueActive = false;
        
        // 重新启用玩家控制
        if (disablePlayerControlDuringDialogue && playerController != null)
        {
            playerController.EnableControl();
            Debug.Log("[DialogueSystem] 对话结束，已重新启用玩家移动控制");
        }
        
        // 重新启用武器系统
        if (disableWeaponsDuringDialogue && weaponManager != null)
        {
            weaponManager.enabled = true;
            Debug.Log("[DialogueSystem] 对话结束，已重新启用武器系统");
        }

        // 触发对话完成事件
        OnDialogueCompleted?.Invoke(dialogueId);
        
        // 如果是任务对话，通知先决条件管理器
        if (isQuestDialogue && PrerequisiteManager.Instance != null)
        {
            if (!string.IsNullOrEmpty(dialogueId))
            {
                PrerequisiteManager.Instance.CompleteDialogue(dialogueId);
                Debug.Log($"[DialogueSystem] 任务对话完成: {dialogueId}");
            }
            
            // 如果设置了完成后启用敌人生成
            if (enableEnemySpawningAfterComplete)
            {
                PrerequisiteManager.Instance.EnableEnemySpawning();
                Debug.Log("[DialogueSystem] 已启用敌人生成");
            }
        }
        
        this.gameObject.SetActive(false);
        NextNPC.SetActive(true);
    }
    void OnTriggerEnter2D(Collider2D Other)
    {
        if (Other.gameObject.tag == "Player")
        {
            isEnter = true;
            Debug.Log("[DialogueSystem] 玩家进入对话区域");
        }
    }
    
    void OnTriggerExit2D(Collider2D Other)
    {
        if (Other.gameObject.tag == "Player")
        {
            isEnter = false;
            Debug.Log("[DialogueSystem] 玩家离开对话区域");
            
            // 如果对话正在进行中，可以选择是否强制结束对话
            // 取消注释下面的代码来启用离开时自动结束对话
            // if (isDialogueActive)
            // {
            //     HideDialogue();
            // }
        }
    }

   
}