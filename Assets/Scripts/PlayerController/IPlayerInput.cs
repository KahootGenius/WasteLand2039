using UnityEngine;

/// <summary>
/// 玩家输入来源（实验分支：让脚本机器人驱动玩家，用于训练场）。
/// PlayerController / WeaponManager 的 InputOverride 不为空时读取它，否则使用原版的键盘鼠标输入，行为不变。
/// 机器人受与真人相同的规则约束：移动速度、移动中不能开火、开火动画期间不能移动等。
/// </summary>
public interface IPlayerInput
{
    /// <summary>移动输入，等价于 (GetAxisRaw("Horizontal"), GetAxisRaw("Vertical"))</summary>
    Vector2 Move { get; }

    /// <summary>瞄准方向（代替鼠标位置）；零向量 = 保持当前方向</summary>
    Vector2 Aim { get; }

    /// <summary>取走一次开火按键（等价于鼠标左键 / 空格按下）。每次按键只返回一次 true</summary>
    bool ConsumeFire();

    /// <summary>取走一次换弹按键（等价于 R 键按下）。每次按键只返回一次 true</summary>
    bool ConsumeReload();
}
