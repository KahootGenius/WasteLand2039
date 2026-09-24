using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class WeaponManager : MonoBehaviour
{
    [Header("武器配置")]
    [SerializeField] private List<RangedWeapon> availableWeapons = new List<RangedWeapon>();
    [SerializeField] private int currentWeaponIndex = 0;
    
    [Header("瞄准设置")]
    [SerializeField] private bool useMouseAiming = true;
    [SerializeField] private Transform aimPoint; // 瞄准点，用于显示瞄准方向
    [SerializeField] private LineRenderer aimLine; // 瞄准线（可选）
    [SerializeField] private float maxAimDistance = 10f;
    [SerializeField] private float firePointDistance = 1f; // FirePoint距离玩家的距离
    
    [Header("自动瞄准设置")]
    [SerializeField] private bool enableAutoAim = false;
    [SerializeField] private float autoAimRange = 5f;
    [SerializeField] private LayerMask enemyLayers = -1;
    
    // 私有变量
    private RangedWeapon currentWeapon;
    private Camera playerCamera;
    private Vector2 aimDirection = Vector2.right;
    private Transform nearestEnemy;
    
    // 事件
    public System.Action<RangedWeapon> OnWeaponChanged;
    public System.Action<Vector2> OnAimDirectionChanged;
    public System.Action OnWeaponFired;
    
    // 属性
    public RangedWeapon CurrentWeapon => currentWeapon;
    public Vector2 AimDirection => aimDirection;
    public bool HasWeapon => currentWeapon != null;
    public int WeaponCount => availableWeapons.Count;
    public int CurrentWeaponIndex => currentWeaponIndex;
    
    private void Awake()
    {
        // 获取摄像机引用
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            playerCamera = FindObjectOfType<Camera>();
        }
        
        // 初始化瞄准点
        if (aimPoint == null)
        {
            GameObject aimObj = new GameObject("AimPoint");
            aimObj.transform.SetParent(transform);
            aimPoint = aimObj.transform;
        }
        
        // 初始化瞄准线
        if (aimLine == null)
        {
            aimLine = GetComponent<LineRenderer>();
        }
    }
    
    private void Start()
    {
        
        // 调试：列出所有可用武器
        for (int i = 0; i < availableWeapons.Count; i++)
        {
            if (availableWeapons[i] != null)
            {

            }
            else
            {

            }
        }
        
        // 设置初始武器
        if (availableWeapons.Count > 0)
        {

            SetCurrentWeapon(currentWeaponIndex);
        }
        else
        {

        }
    }
    
    private void Update()
    {
        // 更新瞄准方向
        UpdateAimDirection();
        
        // 更新瞄准点位置
        UpdateAimPoint();
        
        // 更新瞄准线
        UpdateAimLine();
        
        // 处理射击输入
        HandleFireInput();
        
        // 处理武器切换输入
        HandleWeaponSwitching();
        
        // 处理重装输入
        HandleReloadInput();
    }
    
    /// <summary>
    /// 尝试开火
    /// </summary>
    /// <returns>是否成功开火</returns>
    public bool TryFire()
    {
        if (currentWeapon == null)
        {

            return false;
        }
            
        Vector2 fireDirection = GetFireDirection();

        bool fired = currentWeapon.TryFire(fireDirection);

        
        if (fired)
        {
            OnWeaponFired?.Invoke();
        }
        
        return fired;
    }
    
    /// <summary>
    /// 获取射击方向
    /// </summary>
    /// <returns>射击方向</returns>
    private Vector2 GetFireDirection()
    {
        // 如果启用自动瞄准且找到敌人，瞄准敌人
        if (enableAutoAim && nearestEnemy != null)
        {
            Vector2 directionToEnemy = (nearestEnemy.position - transform.position).normalized;
            return directionToEnemy;
        }
        
        // 否则使用当前瞄准方向
        return aimDirection;
    }
    
    /// <summary>
    /// 更新瞄准方向
    /// </summary>
    private void UpdateAimDirection()
    {
        Vector2 newDirection = aimDirection;
        
        if (useMouseAiming && playerCamera != null)
        {
            // 鼠标瞄准
            Vector3 mouseWorldPos = playerCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0f;
            newDirection = (mouseWorldPos - transform.position).normalized;
        }
        else
        {
            // 键盘瞄准
            float aimX = Input.GetAxisRaw("Horizontal");
            float aimY = Input.GetAxisRaw("Vertical");
            
            if (aimX != 0 || aimY != 0)
            {
                newDirection = new Vector2(aimX, aimY).normalized;
            }
        }
        
        // 更新瞄准方向
        if (newDirection != aimDirection)
        {
            aimDirection = newDirection;
            OnAimDirectionChanged?.Invoke(aimDirection);
            
            // 更新FirePoint位置，使其跟随瞄准方向
            UpdateFirePointPosition();
        }
        
        // 更新自动瞄准
        if (enableAutoAim)
        {
            UpdateAutoAim();
        }
    }
    
    /// <summary>
    /// 更新FirePoint位置
    /// </summary>
    private void UpdateFirePointPosition()
    {
        if (currentWeapon != null && currentWeapon.FirePoint != null)
        {
            Vector3 firePointPosition = transform.position + (Vector3)aimDirection * firePointDistance;
            currentWeapon.FirePoint.position = firePointPosition;
        }
    }
    
    /// <summary>
    /// 更新自动瞄准
    /// </summary>
    private void UpdateAutoAim()
    {
        nearestEnemy = null;
        float nearestDistance = autoAimRange;
        
        // 查找范围内最近的敌人
        Collider2D[] enemies = Physics2D.OverlapCircleAll(transform.position, autoAimRange, enemyLayers);
        
        foreach (Collider2D enemy in enemies)
        {
            if (enemy.transform == transform) continue;
            
            float distance = Vector2.Distance(transform.position, enemy.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestEnemy = enemy.transform;
            }
        }
    }
    
    /// <summary>
    /// 更新瞄准点位置
    /// </summary>
    private void UpdateAimPoint()
    {
        if (aimPoint != null)
        {
            Vector3 aimPosition = transform.position + (Vector3)aimDirection * maxAimDistance;
            aimPoint.position = aimPosition;
        }
    }
    
    /// <summary>
    /// 更新瞄准线
    /// </summary>
    private void UpdateAimLine()
    {
        if (aimLine != null)
        {
            aimLine.positionCount = 2;
            aimLine.SetPosition(0, transform.position);
            aimLine.SetPosition(1, transform.position + (Vector3)aimDirection * maxAimDistance);
        }
    }
    
    /// <summary>
    /// 处理武器切换输入
    /// </summary>
    private void HandleWeaponSwitching()
    {
        // 滚轮切换武器
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f)
        {
            SwitchToNextWeapon();
        }
        else if (scroll < 0f)
        {
            SwitchToPreviousWeapon();
        }
        
        // 数字键切换武器
        for (int i = 1; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                int weaponIndex = i - 1;
                if (weaponIndex < availableWeapons.Count)
                {
                    SetCurrentWeapon(weaponIndex);
                }
            }
        }
    }
    
    /// <summary>
    /// 处理重装输入
    /// </summary>
    private void HandleReloadInput()
    {
        if (Input.GetKeyDown(KeyCode.R) && currentWeapon != null)
        {
            currentWeapon.StartReload();
        }
    }
    
    /// <summary>
    /// 处理射击输入
    /// </summary>
    private void HandleFireInput()
    {
        // 检查左键点击或空格键
        bool mouseInput = Input.GetMouseButtonDown(0);
        bool keyboardInput = Input.GetKeyDown(KeyCode.Space);
        
        // 如果是鼠标输入，检查是否点击在UI上（添加调试信息）
        if (mouseInput)
        {
            Debug.Log("[WeaponManager] 检测到鼠标左键点击");
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                Debug.Log("[WeaponManager] 鼠标点击在UI上，忽略射击输入");
                return;
            }
            else
            {
                Debug.Log("[WeaponManager] 鼠标点击不在UI上，继续处理射击");
            }
        }
        
        if ((mouseInput || keyboardInput) && currentWeapon != null)
        {
            Debug.Log($"[WeaponManager] 准备射击 - 鼠标输入: {mouseInput}, 键盘输入: {keyboardInput}, 当前武器: {currentWeapon.name}");
            
            // 检查角色是否在移动
            var playerController = GetComponentInParent<PlayerController>();
            if (playerController != null)
            {
                // 检查角色是否在移动（通过检查移动方向的大小）
                bool isMoving = playerController.GetComponent<Rigidbody2D>().velocity.magnitude > 0.1f;
                Debug.Log($"[WeaponManager] 角色移动状态: {isMoving}, 速度: {playerController.GetComponent<Rigidbody2D>().velocity.magnitude}");
                if (isMoving)
                {
                    Debug.Log("[WeaponManager] 角色正在移动，取消射击");
                    return;
                }
            }
            
            Debug.Log("[WeaponManager] 尝试射击...");
            bool fired = TryFire();
            Debug.Log($"[WeaponManager] 射击结果: {fired}");
            
            // 如果成功射击，触发攻击动画
            if (fired)
            {
                // 通知PlayerController播放攻击动画
                if (playerController != null)
                {
                    playerController.TriggerAttackAnimation();
                }
                else
                {
                }
            }
        }
        else if (mouseInput || keyboardInput)
        {
            if (currentWeapon == null)
            {
                Debug.Log("[WeaponManager] 无法射击：没有装备武器");
            }
        }
    }
    
    /// <summary>
    /// 切换到下一个武器
    /// </summary>
    public void SwitchToNextWeapon()
    {
        if (availableWeapons.Count <= 1) return;
        
        int nextIndex = (currentWeaponIndex + 1) % availableWeapons.Count;
        SetCurrentWeapon(nextIndex);
    }
    
    /// <summary>
    /// 切换到上一个武器
    /// </summary>
    public void SwitchToPreviousWeapon()
    {
        if (availableWeapons.Count <= 1) return;
        
        int prevIndex = (currentWeaponIndex - 1 + availableWeapons.Count) % availableWeapons.Count;
        SetCurrentWeapon(prevIndex);
    }
    
    /// <summary>
    /// 设置当前武器
    /// </summary>
    /// <param name="weaponIndex">武器索引</param>
    public void SetCurrentWeapon(int weaponIndex)
    {
        
        if (weaponIndex < 0 || weaponIndex >= availableWeapons.Count)
        {
            return;
        }
            
        // 禁用当前武器
        if (currentWeapon != null)
        {
            currentWeapon.gameObject.SetActive(false);
        }
        
        // 设置新武器
        currentWeaponIndex = weaponIndex;
        currentWeapon = availableWeapons[currentWeaponIndex];
        
        
        // 启用新武器
        if (currentWeapon != null)
        {
            currentWeapon.gameObject.SetActive(true);

            OnWeaponChanged?.Invoke(currentWeapon);
            
            // 更新新武器的FirePoint位置
            UpdateFirePointPosition();
        }
        else
        {

        }
    }
    
    /// <summary>
    /// 添加武器
    /// </summary>
    /// <param name="weapon">要添加的武器</param>
    public void AddWeapon(RangedWeapon weapon)
    {
        if (weapon != null && !availableWeapons.Contains(weapon))
        {
            availableWeapons.Add(weapon);
            weapon.transform.SetParent(transform);
            weapon.gameObject.SetActive(false);
            
            // 如果这是第一个武器，设置为当前武器
            if (availableWeapons.Count == 1)
            {
                SetCurrentWeapon(0);
            }
        }
    }
    
    /// <summary>
    /// 移除武器
    /// </summary>
    /// <param name="weapon">要移除的武器</param>
    public void RemoveWeapon(RangedWeapon weapon)
    {
        if (weapon != null && availableWeapons.Contains(weapon))
        {
            int weaponIndex = availableWeapons.IndexOf(weapon);
            availableWeapons.Remove(weapon);
            
            // 如果移除的是当前武器，切换到下一个
            if (weapon == currentWeapon)
            {
                if (availableWeapons.Count > 0)
                {
                    int newIndex = Mathf.Min(currentWeaponIndex, availableWeapons.Count - 1);
                    SetCurrentWeapon(newIndex);
                }
                else
                {
                    currentWeapon = null;
                    currentWeaponIndex = 0;
                }
            }
            else if (weaponIndex < currentWeaponIndex)
            {
                currentWeaponIndex--;
            }
        }
    }
    
    /// <summary>
    /// 设置瞄准模式
    /// </summary>
    /// <param name="useMouseAim">是否使用鼠标瞄准</param>
    public void SetAimingMode(bool useMouseAim)
    {
        useMouseAiming = useMouseAim;
    }
    
    /// <summary>
    /// 设置自动瞄准
    /// </summary>
    /// <param name="enabled">是否启用自动瞄准</param>
    public void SetAutoAim(bool enabled)
    {
        enableAutoAim = enabled;
    }
    
    private void OnDrawGizmosSelected()
    {
        // 绘制自动瞄准范围
        if (enableAutoAim)
        {
            Gizmos.color = Color.yellow;
            DrawWireCircle(transform.position, autoAimRange);
        }
        
        // 绘制瞄准方向
        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, aimDirection * maxAimDistance);
    }
    
    /// <summary>
    /// 绘制线框圆形
    /// </summary>
    /// <param name="center">圆心</param>
    /// <param name="radius">半径</param>
    private void DrawWireCircle(Vector3 center, float radius)
    {
        int segments = 32;
        float angleStep = 360f / segments;
        Vector3 prevPoint = center + Vector3.right * radius;
        
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 newPoint = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }
    }
}