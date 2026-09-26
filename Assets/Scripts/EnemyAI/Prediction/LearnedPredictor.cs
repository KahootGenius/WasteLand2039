using System;

/// <summary>
/// 学习型预测器的参数（由 MLTraining/predictor/train_predictor.py 离线训练，保存为 JSON；
/// Unity 侧用 JsonUtility 读取 TextAsset）。softmax 回归：logits = W · ((x - mean) / scale) + bias
/// </summary>
[Serializable]
public class LearnedPredictorWeights
{
    public const int CurrentVersion = 2;

    /// <summary>2：输出 8 个逃跑方向（1 为终点区域，已不再使用）</summary>
    public int version = CurrentVersion;
    /// <summary>训练时场景的区域数（决定特征向量长度）</summary>
    public int zoneCount;
    public int featureCount;
    /// <summary>输出类别数（方向扇区数 = EscapeSectors.Count）</summary>
    public int classCount;
    /// <summary>classCount × featureCount，按行（每个方向一行）</summary>
    public float[] weights;
    public float[] bias;
    /// <summary>特征标准化（可为空 = 不标准化）</summary>
    public float[] featureMean;
    public float[] featureScale;
    public string trainedOn;
    public string notes;
}

/// <summary>
/// 学习型预测器（V2 的"监督学习"部分）：离线训练的多项 logistic 回归，输入 EscapeFeatures
/// （玩家画像 + 当前区域 + 威胁方向），输出下一次逃跑的方向分布（8 扇区）。纯 C# 推理，不需要 Sentis
/// </summary>
public class LearnedPredictor : IEscapePredictor
{
    private readonly LearnedPredictorWeights model;
    private readonly float[] normalized;

    public string Name => "Learned";
    public int ZoneCount => model.zoneCount;

    public LearnedPredictor(LearnedPredictorWeights weights)
    {
        string error = Validate(weights);
        if (error != null)
            throw new ArgumentException("预测器参数无效：" + error);
        model = weights;
        normalized = new float[weights.featureCount];
    }

    /// <summary>检查参数是否完整；有问题时返回原因，否则返回 null</summary>
    public static string Validate(LearnedPredictorWeights weights)
    {
        if (weights == null)
            return "没有数据";
        if (weights.version != LearnedPredictorWeights.CurrentVersion)
            return $"不支持的版本 {weights.version}（需要 {LearnedPredictorWeights.CurrentVersion}，请重新训练）";
        if (weights.zoneCount <= 0 || weights.featureCount != EscapeFeatures.Size(weights.zoneCount))
            return $"特征数 {weights.featureCount} 与区域数 {weights.zoneCount} 不符（应为 {EscapeFeatures.Size(Math.Max(1, weights.zoneCount))}）";
        if (weights.classCount != EscapeSectors.Count)
            return $"输出类别数 {weights.classCount} ≠ {EscapeSectors.Count}";
        if (weights.weights == null || weights.weights.Length != weights.classCount * weights.featureCount)
            return "权重矩阵大小不符";
        if (weights.bias == null || weights.bias.Length != weights.classCount)
            return "偏置长度不符";
        bool hasMean = weights.featureMean != null && weights.featureMean.Length > 0;
        bool hasScale = weights.featureScale != null && weights.featureScale.Length > 0;
        if (hasMean != hasScale || (hasMean && (weights.featureMean.Length != weights.featureCount || weights.featureScale.Length != weights.featureCount)))
            return "标准化参数长度不符";
        return null;
    }

    public void Predict(PredictionInput input, float[] directionProbabilities)
    {
        float[] x = input.Features;
        bool standardize = model.featureMean != null && model.featureMean.Length == model.featureCount;
        for (int f = 0; f < model.featureCount; f++)
        {
            float value = x[f];
            if (standardize)
                value = (value - model.featureMean[f]) / (model.featureScale[f] > 1e-6f ? model.featureScale[f] : 1f);
            normalized[f] = value;
        }

        float max = float.NegativeInfinity;
        for (int c = 0; c < model.classCount; c++)
        {
            float logit = model.bias[c];
            int row = c * model.featureCount;
            for (int f = 0; f < model.featureCount; f++)
                logit += model.weights[row + f] * normalized[f];
            directionProbabilities[c] = logit;
            if (logit > max)
                max = logit;
        }

        float sum = 0f;
        for (int c = 0; c < model.classCount; c++)
        {
            directionProbabilities[c] = (float)Math.Exp(directionProbabilities[c] - max);
            sum += directionProbabilities[c];
        }
        for (int c = 0; c < model.classCount; c++)
            directionProbabilities[c] /= sum;
    }
}
