// Tests for saving / restoring the player profile across sessions (PlayerProfile.ExportState /
// TryImportState). The game writes the state with Unity's JsonUtility (PlayerProfileStore); here a
// System.Text.Json round trip over the public fields stands in for the file.

using System;
using System.Linq;
using System.Text.Json;

static partial class LogicTests
{
    static void ProfilePersistenceTests()
    {
        Console.WriteLine("Profile persistence");
        var session1 = new Sim(11);
        for (int round = 0; round < 10; round++) session1.RunRound(180f, 0f, 4f, shootWhileRunning: false);
        PlayerProfile original = session1.Profile;

        PlayerProfileState state = original.ExportState();
        var json = new JsonSerializerOptions { IncludeFields = true };
        PlayerProfileState fromFile = JsonSerializer.Deserialize<PlayerProfileState>(JsonSerializer.Serialize(state, json), json);

        var restored = new PlayerProfile(original.ZoneCount);
        bool imported = restored.TryImportState(fromFile, out string error);
        Check(imported, "state survives a JSON round trip and imports", error ?? "");
        Check(restored.Engagements == original.Engagements && restored.Escapes == original.Escapes,
            "engagement / escape counts restored", $"= {restored.Engagements} / {restored.Escapes}");
        Check(original.ToObservation().SequenceEqual(restored.ToObservation()), "observation vector identical after restore");
        original.TryGetTopRoute(out int from1, out int to1, out float share1);
        restored.TryGetTopRoute(out int from2, out int to2, out float share2);
        Check(from1 == from2 && to1 == to2 && Math.Abs(share1 - share2) < 1e-6, "top route restored",
            $"= {session1.Zones.GetZoneName(from2)} → {session1.Zones.GetZoneName(to2)}");

        // The next session keeps learning from where the last one stopped: same updates, same profile
        var session2 = new Sim(12);
        for (int round = 0; round < 5; round++) session2.RunRound(270f, 90f, 4f, shootWhileRunning: false);
        foreach (EscapeEpisode escape in session2.EscapeLog)
        {
            original.AddEscape(escape);
            restored.AddEscape(escape);
        }
        float[] a = original.ToObservation(), b = restored.ToObservation();
        Check(a.Zip(b, (x, y) => Math.Abs(x - y)).Max() < 1e-5f, "restored profile keeps updating like the original",
            $"(after {session2.EscapeLog.Count} more escapes, top zone {session1.Zones.GetZoneName(restored.TopEscapeZones(1)[0])})");
        Check(state.escapes == session1.ValidEscapes && original.Escapes > state.escapes &&
              !state.destinationWeights.SequenceEqual(original.ExportState().destinationWeights),
            "exported state is a copy (later updates don't change it)", $"= {state.escapes} vs now {original.Escapes} escapes");

        var otherMap = new PlayerProfile(original.ZoneCount + 1);
        Check(!otherMap.TryImportState(state, out string mismatch) && otherMap.Escapes == 0,
            "different zone map: rejected, profile untouched", $"({mismatch})");
        var broken = original.ExportState();
        broken.routeTo = new int[0];
        Check(!new PlayerProfile(original.ZoneCount).TryImportState(broken, out string incomplete), "incomplete route data rejected",
            $"({incomplete})");
        Check(!new PlayerProfile(original.ZoneCount).TryImportState(null, out _), "missing state rejected");
    }
}
