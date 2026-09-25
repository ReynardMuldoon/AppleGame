using System;
using System.IO;
using AppleNet;
using UnityEditor;
using UnityEngine;

public static class AppleMultiplayerValidation
{
    [MenuItem("Apple Multiplayer/Validate rules and protocol")]
    public static void Run()
    {
        var random = new System.Random(123);
        for (int i = 0; i < 10000; i++)
        {
            var board = LocalBoardRules.Generate(random);
            int sum = 0;
            Check(board.Length == 170, "Board size");
            foreach (byte v in board)
            {
                Check(v >= 1 && v <= 9, "Value range");
                sum += v;
            }
            Check(sum % 10 == 0, "Total must be divisible by ten");
        }
        var w = new PacketWriter();
        w.U8(7);
        w.U16(0x1234);
        w.U32(0x89abcdef);
        w.Text("Apple 사과");
        var data = w.ToArray();
        Check(data[1] == 0x34 && data[2] == 0x12, "Little-endian");
        var r = new PacketReader(data);
        Check(r.U8() == 7 && r.U16() == 0x1234 && r.U32() == 0x89abcdef, "Scalar codec");
        Check(r.Text(48) == "Apple 사과", "UTF-8");
        r.End();
        bool rejected = false;
        try
        {
            new PacketReader(new byte[1]).U32();
        }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Short packet rejection");
        var grid = new int[170];
        grid[0] = 2;
        grid[1] = 8;
        Check(AppleGameSolver.HasAvailableMoves(grid), "Rectangle move");
        grid[1] = 0;
        Check(!AppleGameSolver.HasAvailableMoves(grid), "No legal move");
        foreach (string scene in new[] { "TitleScene", "GameScene" })
        {
            bool found = false;
            foreach (var entry in EditorBuildSettings.scenes)
                if (entry.enabled && Path.GetFileNameWithoutExtension(entry.path) == scene)
                    found = true;
            if (!found)
                Debug.LogWarning("Add " + scene + " to the active Build Profile scene list.");
        }
        Debug.Log("PASS: 10000 local boards, packet codec, malformed input, solver. This does not test Unity networking or scene UI.");
    }
    private static void Check(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException("Apple validation failed: " + message);
    }
}
