using System;

/// <summary>
/// 学习型预测器的参数（由 MLTraining/predictor/train_predictor.py 离线训练，保存为 JSON；
/// Unity 侧用 JsonUtility 读取 TextAsset）。softmax 回归：logits = W · ((x - mean) / scale) + bias
/// </summary>
[Serializable]
public class LearnedPredictorWeights
{
    public int version = 1;
    public int zoneCount;
    public int featureCount;
    /// <summary>zoneCount × featureCount，按行（每个区域一行）</summary>
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
/// （玩家画像 + 当前区域 + 威胁方向），输出下一次逃跑的终点区域分布。纯 C# 推理，不需要 Sentis
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
        if (weights.version != 1)
            return $"不支持的版本 {weights.version}";
        if (weights.zoneCount <= 0 || weights.featureCount != EscapeFeatures.Size(weights.zoneCount))
            return $"特征数 {weights.featureCount} 与区域数 {weights.zoneCount} 不符（应为 {EscapeFeatures.Size(Math.Max(1, weights.zoneCount))}）";
        if (weights.weights == null || weights.weights.Length != weights.zoneCount * weights.featureCount)
            return "权重矩阵大小不符";
        if (weights.bias == null || weights.bias.Length != weights.zoneCount)
            return "偏置长度不符";
        bool hasMean = weights.featureMean != null && weights.featureMean.Length > 0;
        bool hasScale = weights.featureScale != null && weights.featureScale.Length > 0;
        if (hasMean != hasScale || (hasMean && (weights.featureMean.Length != weights.featureCount || weights.featureScale.Length != weights.featureCount)))
            return "标准化参数长度不符";
        return null;
    }

    public void Predict(PredictionInput input, float[] zoneProbabilities)
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
        for (int zone = 0; zone < model.zoneCount; zone++)
        {
            float logit = model.bias[zone];
            int row = zone * model.featureCount;
            for (int f = 0; f < model.featureCount; f++)
                logit += model.weights[row + f] * normalized[f];
            zoneProbabilities[zone] = logit;
            if (logit > max)
                max = logit;
        }

        float sum = 0f;
        for (int zone = 0; zone < model.zoneCount; zone++)
        {
            zoneProbabilities[zone] = (float)Math.Exp(zoneProbabilities[zone] - max);
            sum += zoneProbabilities[zone];
        }
        for (int zone = 0; zone < model.zoneCount; zone++)
            zoneProbabilities[zone] /= sum;
    }
}
