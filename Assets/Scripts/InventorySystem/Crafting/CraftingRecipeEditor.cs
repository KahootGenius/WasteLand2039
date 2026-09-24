#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(CraftingRecipe))]
public class CraftingRecipeEditor : Editor
{
    private SerializedProperty recipeNameProp;
    private SerializedProperty descriptionProp;
    private SerializedProperty recipeIconProp;
    private SerializedProperty ingredientsProp;
    private SerializedProperty resultItemProp;
    private SerializedProperty resultAmountProp;
    private SerializedProperty isUnlockedProp;
    private SerializedProperty craftingTimeProp;
    private SerializedProperty experienceRewardProp;
    
    private bool showIngredients = true;
    private bool showSettings = true;
    
    private void OnEnable()
    {
        recipeNameProp = serializedObject.FindProperty("recipeName");
        descriptionProp = serializedObject.FindProperty("description");
        recipeIconProp = serializedObject.FindProperty("recipeIcon");
        ingredientsProp = serializedObject.FindProperty("ingredients");
        resultItemProp = serializedObject.FindProperty("resultItem");
        resultAmountProp = serializedObject.FindProperty("resultAmount");
        isUnlockedProp = serializedObject.FindProperty("isUnlocked");
        craftingTimeProp = serializedObject.FindProperty("craftingTime");
        experienceRewardProp = serializedObject.FindProperty("experienceReward");
    }
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        CraftingRecipe recipe = (CraftingRecipe)target;
        
        // 标题
        EditorGUILayout.Space();
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.fontSize = 16;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        EditorGUILayout.LabelField("合成配方编辑器", titleStyle);
        EditorGUILayout.Space();
        
        // 基本信息
        EditorGUILayout.LabelField("基本信息", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        
        EditorGUILayout.PropertyField(recipeNameProp, new GUIContent("配方名称"));
        EditorGUILayout.PropertyField(descriptionProp, new GUIContent("配方描述"));
        EditorGUILayout.PropertyField(recipeIconProp, new GUIContent("配方图标"));
        
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();
        
        // 合成结果
        EditorGUILayout.LabelField("合成结果", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        
        EditorGUILayout.PropertyField(resultItemProp, new GUIContent("结果物品"));
        EditorGUILayout.PropertyField(resultAmountProp, new GUIContent("结果数量"));
        
        // 显示结果预览
        if (recipe.resultItem != null)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预览:", GUILayout.Width(50));
            
            if (recipe.resultItem.icon != null)
            {
                GUILayout.Label(recipe.resultItem.icon.texture, GUILayout.Width(32), GUILayout.Height(32));
            }
            
            EditorGUILayout.LabelField($"{recipe.resultItem.itemName} x{recipe.resultAmount}");
            EditorGUILayout.EndHorizontal();
        }
        
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();
        
        // 材料需求
        showIngredients = EditorGUILayout.Foldout(showIngredients, "材料需求", true, EditorStyles.foldoutHeader);
        if (showIngredients)
        {
            EditorGUI.indentLevel++;
            
            EditorGUILayout.PropertyField(ingredientsProp, new GUIContent("材料列表"), true);
            
            // 显示材料总览
            if (recipe.ingredients != null && recipe.ingredients.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("材料总览:", EditorStyles.boldLabel);
                
                foreach (var ingredient in recipe.ingredients)
                {
                    if (ingredient.item != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        
                        if (ingredient.item.icon != null)
                        {
                            GUILayout.Label(ingredient.item.icon.texture, GUILayout.Width(24), GUILayout.Height(24));
                        }
                        else
                        {
                            GUILayout.Space(28);
                        }
                        
                        EditorGUILayout.LabelField($"{ingredient.item.itemName} x{ingredient.amount}");
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.Space();
        
        // 配方设置
        showSettings = EditorGUILayout.Foldout(showSettings, "配方设置", true, EditorStyles.foldoutHeader);
        if (showSettings)
        {
            EditorGUI.indentLevel++;
            
            EditorGUILayout.PropertyField(isUnlockedProp, new GUIContent("默认解锁"));
            EditorGUILayout.PropertyField(craftingTimeProp, new GUIContent("合成时间(秒)"));
            EditorGUILayout.PropertyField(experienceRewardProp, new GUIContent("经验奖励"));
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.Space();
        
        // 验证按钮
        if (GUILayout.Button("验证配方", GUILayout.Height(30)))
        {
            ValidateRecipe(recipe);
        }
        
        // 快速创建按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("添加材料"))
        {
            recipe.ingredients.Add(new CraftingIngredient());
            EditorUtility.SetDirty(recipe);
        }
        
        if (GUILayout.Button("清空材料"))
        {
            if (EditorUtility.DisplayDialog("确认", "确定要清空所有材料吗？", "确定", "取消"))
            {
                recipe.ingredients.Clear();
                EditorUtility.SetDirty(recipe);
            }
        }
        EditorGUILayout.EndHorizontal();
        
        serializedObject.ApplyModifiedProperties();
    }
    
    private void ValidateRecipe(CraftingRecipe recipe)
    {
        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        
        // 检查基本信息
        if (string.IsNullOrEmpty(recipe.recipeName))
            errors.Add("配方名称不能为空");
            
        if (recipe.resultItem == null)
            errors.Add("必须设置结果物品");
            
        if (recipe.resultAmount <= 0)
            errors.Add("结果数量必须大于0");
        
        // 检查材料
        if (recipe.ingredients == null || recipe.ingredients.Count == 0)
        {
            warnings.Add("配方没有材料需求");
        }
        else
        {
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                var ingredient = recipe.ingredients[i];
                if (ingredient.item == null)
                    errors.Add($"材料 {i + 1} 的物品未设置");
                if (ingredient.amount <= 0)
                    errors.Add($"材料 {i + 1} 的数量必须大于0");
            }
        }
        
        // 检查合成时间
        if (recipe.craftingTime < 0)
            warnings.Add("合成时间为负数");
        
        // 显示结果
        if (errors.Count > 0)
        {
            string errorMessage = "发现以下错误:\n" + string.Join("\n", errors);
            EditorUtility.DisplayDialog("配方验证失败", errorMessage, "确定");
        }
        else if (warnings.Count > 0)
        {
            string warningMessage = "发现以下警告:\n" + string.Join("\n", warnings);
            EditorUtility.DisplayDialog("配方验证通过(有警告)", warningMessage, "确定");
        }
        else
        {
            EditorUtility.DisplayDialog("配方验证通过", "配方设置正确，没有发现问题！", "确定");
        }
    }
}

/// <summary>
/// 配方创建向导
/// </summary>
public class CraftingRecipeWizard : ScriptableWizard
{
    public string recipeName = "新配方";
    public string description = "配方描述";
    public Item resultItem;
    public int resultAmount = 1;
    
    [MenuItem("Assets/Create/Inventory/Crafting Recipe Wizard")]
    static void CreateWizard()
    {
        ScriptableWizard.DisplayWizard<CraftingRecipeWizard>("创建合成配方", "创建");
    }
    
    void OnWizardCreate()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "保存配方",
            recipeName + ".asset",
            "asset",
            "选择保存位置");
            
        if (!string.IsNullOrEmpty(path))
        {
            CraftingRecipe recipe = CreateInstance<CraftingRecipe>();
            recipe.recipeName = recipeName;
            recipe.description = description;
            recipe.resultItem = resultItem;
            recipe.resultAmount = resultAmount;
            
            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();
            
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = recipe;
        }
    }
    
    void OnWizardUpdate()
    {
        helpString = "创建一个新的合成配方资源文件";
        isValid = !string.IsNullOrEmpty(recipeName) && resultItem != null && resultAmount > 0;
    }
}
#endif