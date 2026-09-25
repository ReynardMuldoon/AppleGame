using AppleNet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Functional IMGUI lobby: no new scene/prefab references are required.
// Existing serialized fields stay intact so this file can replace the old script.
public class TitleUI : MonoBehaviour
{
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private TextMeshProUGUI bestScoreText;
    private string host = "127.0.0.1", nickname = "Player", code = "";
    private bool multiMenu, force;
    private void Start()
    {
        NetworkManager.Ensure();
        if (gameStartButton != null)
        {
            // The original full-screen overlay canvas could cover IMGUI on some layouts.
            var canvas = gameStartButton.GetComponentInParent<Canvas>();
            if (canvas != null)
                canvas.rootCanvas.enabled = false;
            gameStartButton.gameObject.SetActive(false);
        }
        if (exitButton != null)
            exitButton.gameObject.SetActive(false);
        if (bestScoreText != null)
            bestScoreText.gameObject.SetActive(false);
    }
    private void OnGUI()
    {
        var n = NetworkManager.Ensure();
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 640f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
        GUILayout.BeginArea(new Rect(220, 55, 520, 530), GUI.skin.box);
        GUILayout.Label("APPLE GAME");
        if (n.CurrentRoom != null)
            DrawRoom(n);
        else if (!multiMenu && !n.IsConnected && !n.IsConnecting)
        {
            GUILayout.Label("Best score: " + PlayerPrefs.GetInt(GameConstants.BEST_SCORE_KEY, 0));
            if (GUILayout.Button("Single player (offline)", GUILayout.Height(48)))
                n.StartSingle();
            if (GUILayout.Button("Multiplayer", GUILayout.Height(48)))
                multiMenu = true;
            if (GUILayout.Button("Exit"))
                Exit();
        }
        else
        {
            GUILayout.Label("Server address (TCP 7777)");
            host = GUILayout.TextField(host, 128);
            GUILayout.Label("Nickname (48 UTF-8 bytes max)");
            nickname = GUILayout.TextField(nickname, 24);
            if (!n.IsConnected)
            {
                GUI.enabled = !n.IsConnecting;
                if (GUILayout.Button(n.IsConnecting ? "Connecting..." : "Connect"))
                    Connect();
                GUI.enabled = true;
            }
            else if (!n.Handshaken)
                GUILayout.Label("Connecting to game service...");
            else
            {
                GUI.enabled = !n.CommandPending;
                if (GUILayout.Button("Create room"))
                    n.CreateRoom();
                GUILayout.Label("Room code");
                code = GUILayout.TextField(code, 4).ToUpperInvariant();
                if (GUILayout.Button("Join room"))
                    n.JoinRoom(code);
                GUI.enabled = true;
            }
            if (GUILayout.Button("Back"))
            {
                n.Disconnect();
                multiMenu = false;
            }
        }
        if (!string.IsNullOrEmpty(n.LastError))
            GUILayout.Label(n.LastError);
        GUILayout.EndArea();
        GUI.matrix = previous;
    }
    private async void Connect()
    {
        await NetworkManager.Ensure().ConnectAsync(host, 7777, nickname);
    }
    private void DrawRoom(NetworkManager n)
    {
        var room = n.CurrentRoom;
        GUILayout.Label("Room: " + room.Code + "   " + room.Members.Count + " / 8");
        foreach (var p in room.Members)
            GUILayout.Label((p.Id == n.OwnId ? "[YOU] " : "") + p.Name + (p.Id == room.Host ? " [HOST]" : "") + (p.Ready ? " - READY" : ""));
        GUI.enabled = !n.CommandPending && room.Phase == RoomPhase.Waiting;
        if (room.Host == n.OwnId)
        {
            force = GUILayout.Toggle(force, "Start even if other players are not ready");
            if (GUILayout.Button("Start game"))
                n.StartRoom(force);
        }
        else
        {
            var me = room.Find(n.OwnId);
            if (GUILayout.Button(me != null && me.Ready ? "Cancel ready" : "Ready"))
                n.SetReady(me == null || !me.Ready);
        }
        GUI.enabled = !n.CommandPending;
        if (GUILayout.Button("Leave room"))
            n.LeaveRoom();
        GUI.enabled = true;
    }
    private void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
