using UnityEngine;

/// <summary>
/// 训练场的一条逃跑路线：从基地出发，终点是本物体所在的避难点。
/// ArenaEnvironment 的路线列表顺序即 A, B, C…，与性格资产的路线偏好一一对应。
/// </summary>
public class ArenaRoute : MonoBehaviour
{
    [Tooltip("路线名称；为空时按在列表中的顺序称为 A, B, C…")]
    [SerializeField] private string routeName = "";
    [SerializeField] private Color gizmoColor = new Color(1f, 0.6f, 0.1f);
    [Tooltip("到达半径（与 BotBrain 一致，仅用于显示）")]
    [SerializeField] private float arriveRadius = 2f;

    public string RouteName(int index)
    {
        return string.IsNullOrEmpty(routeName) ? ((char)('A' + index)).ToString() : routeName;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        if (transform.parent != null)
            Gizmos.DrawLine(transform.parent.position, transform.position);
        Gizmos.DrawWireSphere(transform.position, arriveRadius);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position, string.IsNullOrEmpty(routeName) ? name : routeName);
#endif
    }
}
