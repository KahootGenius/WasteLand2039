using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 遥测文件写入（纯 IO，不依赖 Unity）。一个会话一个目录：
/// - samples.csv   固定频率的玩家/交战状态采样（表头见 SampleHeader）
/// - events.jsonl  事件，每行一个 JSON 对象（波次、生成、击杀、交战、逃跑、画像快照……）
/// - 其他 JSON 文件（session.json、profile_final.json）通过 WriteJsonFile 写入
/// 数字一律使用 InvariantCulture（小数点为 "."），避免系统区域设置影响数据分析。
/// </summary>
public class TelemetryWriter : IDisposable
{
    public const string SampleHeader =
        "t,wave,px,py,vx,vy,hp,zone,state,engagement,escape,near,nearest,retreat,shot_rate,shots,damage,enemies";

    public string DirectoryPath { get; }
    public int SampleCount { get; private set; }
    public int EventCount { get; private set; }

    private readonly StreamWriter samples;
    private readonly StreamWriter events;
    private readonly StringBuilder line = new StringBuilder(256);
    private bool disposed;

    public TelemetryWriter(string directoryPath)
    {
        DirectoryPath = directoryPath;
        Directory.CreateDirectory(directoryPath);

        var utf8 = new UTF8Encoding(false);
        samples = new StreamWriter(Path.Combine(directoryPath, "samples.csv"), false, utf8);
        events = new StreamWriter(Path.Combine(directoryPath, "events.jsonl"), false, utf8);
        samples.WriteLine(SampleHeader);
    }

    /// <summary>写一行采样；参数顺序与 SampleHeader 一致</summary>
    public void WriteSample(float time, int wave, float px, float py, float vx, float vy, float hp,
        string zone, string state, int engagement, int escape, int near, float nearest,
        float retreat, float shotRate, int shots, float damage, int enemies)
    {
        if (disposed)
            return;

        // 每个字段都经过 Field(...)，保证数字格式与区域设置无关
        line.Clear();
        Field(time); Field(wave); Field(px); Field(py); Field(vx); Field(vy); Field(hp);
        Field(zone); Field(state); Field(engagement); Field(escape); Field(near);
        if (float.IsInfinity(nearest)) Field(""); else Field(nearest);
        Field(retreat); Field(shotRate); Field(shots); Field(damage); Field(enemies);

        samples.WriteLine(line.ToString());
        SampleCount++;
    }

    /// <summary>开始一条事件；用返回的 JsonLine 添加字段后调用 Write()</summary>
    public JsonLine Event(string type, float time)
    {
        return new JsonLine(this).Add("t", time).Add("type", type);
    }

    public void WriteJsonFile(string fileName, string json)
    {
        File.WriteAllText(Path.Combine(DirectoryPath, fileName), json, new UTF8Encoding(false));
    }

    public void Flush()
    {
        if (disposed)
            return;
        samples.Flush();
        events.Flush();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        samples.Dispose();
        events.Dispose();
    }

    internal void WriteEventLine(string json)
    {
        if (disposed)
            return;
        events.WriteLine(json);
        EventCount++;
    }

    private void Field(float value)
    {
        Separator();
        line.Append(Format(value));
    }

    private void Field(int value)
    {
        Separator();
        line.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private void Field(string value)
    {
        Separator();
        line.Append(value);
    }

    private void Separator()
    {
        if (line.Length > 0)
            line.Append(',');
    }

    internal static string Format(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return "null";
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// 单行 JSON 对象构建器（只支持扁平字段和数组，满足遥测需要）
/// </summary>
public class JsonLine
{
    private readonly TelemetryWriter writer;
    private readonly StringBuilder json = new StringBuilder(256);
    private bool first = true;

    internal JsonLine(TelemetryWriter writer)
    {
        this.writer = writer;
        json.Append('{');
    }

    /// <summary>不写入文件，只构建 JSON 字符串（用于 WriteJsonFile）</summary>
    public static JsonLine Standalone()
    {
        return new JsonLine(null);
    }

    public JsonLine Add(string key, string value)
    {
        Key(key);
        AppendString(value);
        return this;
    }

    public JsonLine Add(string key, float value)
    {
        Key(key);
        json.Append(TelemetryWriter.Format(value));
        return this;
    }

    public JsonLine Add(string key, int value)
    {
        Key(key);
        json.Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public JsonLine Add(string key, bool value)
    {
        Key(key);
        json.Append(value ? "true" : "false");
        return this;
    }

    public JsonLine Add(string key, IList<float> values)
    {
        Key(key);
        json.Append('[');
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) json.Append(',');
            json.Append(TelemetryWriter.Format(values[i]));
        }
        json.Append(']');
        return this;
    }

    public JsonLine Add(string key, IList<string> values)
    {
        Key(key);
        json.Append('[');
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) json.Append(',');
            AppendString(values[i]);
        }
        json.Append(']');
        return this;
    }

    public override string ToString()
    {
        return json.ToString() + "}";
    }

    /// <summary>写入 events.jsonl</summary>
    public void Write()
    {
        writer?.WriteEventLine(ToString());
    }

    private void Key(string key)
    {
        if (!first)
            json.Append(',');
        first = false;
        AppendString(key);
        json.Append(':');
    }

    private void AppendString(string value)
    {
        if (value == null)
        {
            json.Append("null");
            return;
        }

        json.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        json.Append(c);
                    break;
            }
        }
        json.Append('"');
    }
}
