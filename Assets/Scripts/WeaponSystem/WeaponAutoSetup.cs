using UnityEngine;
using System.Collections;

public class WeaponAutoSetup : MonoBehaviour
{
    private void Start()
    {
        // 启动协程来设置武器
        StartCoroutine(SetupWeapon());
    }
    
    private System.Collections.IEnumerator SetupWeapon()
    {
        // 等待一帧确保所有组件都已初始化
        yield return null;
        
        
        WeaponManager weaponManager = FindObjectOfType<WeaponManager>();
        if (weaponManager == null)
        {
            yield break;
        }
        
        // 检查是否已经有武器
        if (weaponManager.WeaponCount > 0)
        {
            // 输出现有武器信息
            for (int i = 0; i < weaponManager.WeaponCount; i++)
            {
                var existingWeapon = weaponManager.transform.GetChild(i).GetComponent<RangedWeapon>();
                if (existingWeapon != null)
                {
                }
            }
            yield break;
        }
        
        // 加载子弹预制体
        GameObject bulletPrefab = Resources.Load<GameObject>("Bullet");
        if (bulletPrefab == null)
        {
            yield break;
        }
        
        // 创建测试武器
        GameObject weaponObj = new GameObject("AutoWeapon");
        weaponObj.transform.SetParent(weaponManager.transform);
        weaponObj.transform.localPosition = Vector3.zero;
        
        // 创建FirePoint
        GameObject firePointObj = new GameObject("FirePoint");
        firePointObj.transform.SetParent(weaponObj.transform);
        firePointObj.transform.localPosition = new Vector3(1f, 0f, 0f);
        
        // 添加RangedWeapon组件
        RangedWeapon weapon = weaponObj.AddComponent<RangedWeapon>();
        
        // 等待一帧确保Awake已执行
        yield return null;
        
        // 设置武器属性
        WeaponStats stats = new WeaponStats
        {
            weaponName = "自动武器",
            damage = 25f,
            fireRate = 5f,  // 提高射击频率：每秒5发，射击间隔0.2秒
            magazineSize = 30,  // 增加弹匣容量
            reloadTime = 1.5f,  // 减少重装时间
            bulletSpeed = 10f,
            bulletLifetime = 3f,
            accuracy = 0.95f,
            maxSpread = 5f
        };
        
        weapon.SetWeaponStats(stats);
        
        weapon.SetFirePoint(firePointObj.transform);
        
        weapon.SetBulletPrefab(bulletPrefab);
        
        // 添加武器到WeaponManager
        weaponManager.AddWeapon(weapon);
        
        
        yield return null;
    }
}