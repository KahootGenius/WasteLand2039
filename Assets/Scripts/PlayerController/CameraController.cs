using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("目标设置")]
    [SerializeField] private Transform target; // 跟随的目标（通常是玩家）
    
    [Header("跟随设置")]
    [SerializeField] private float smoothSpeed = 5f; // 相机跟随的平滑度
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10); // 相机与目标的偏移量
    
    [Header("边界限制")]
    [SerializeField] private bool enableBounds = false; // 是否启用边界限制
    [SerializeField] private float minX = -10f; // 左边界
    [SerializeField] private float maxX = 10f;  // 右边界
    [SerializeField] private float minY = -10f; // 下边界
    [SerializeField] private float maxY = 10f;  // 上边界
    
    private void LateUpdate()
    {
        if (target == null)
            return;
            
        // 计算目标位置
        Vector3 desiredPosition = target.position + offset;
        
        // 应用平滑移动
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        
        // 如果启用边界限制，确保相机位置在边界内
        if (enableBounds)
        {
            smoothedPosition.x = Mathf.Clamp(smoothedPosition.x, minX, maxX);
            smoothedPosition.y = Mathf.Clamp(smoothedPosition.y, minY, maxY);
        }
        
        // 更新相机位置，保持z轴不变
        smoothedPosition.z = offset.z;
        transform.position = smoothedPosition;
    }
    
    // 设置跟随目标
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    // 设置边界
    public void SetBounds(float minX, float maxX, float minY, float maxY)
    {
        this.minX = minX;
        this.maxX = maxX;
        this.minY = minY;
        this.maxY = maxY;
    }
}