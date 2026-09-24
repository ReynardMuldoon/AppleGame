using AppleNet;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MultiplayerHud : MonoBehaviour
{
    public static Rect PanelRect
    {
        get { return new Rect(Mathf.Max(0,Screen.width-255),0,255,Screen.height); }
    }
    public static bool BlocksPointer(Vector2 screenPosition)
    {
        var n=NetworkManager.Instance;
        return n!=null&&n.Multiplayer&&PanelRect.Contains(new Vector2(screenPosition.x,Screen.height-screenPosition.y));
    }
    private void OnGUI()
    {
        var n=NetworkManager.Instance;
        if(n==null||!n.Multiplayer||n.CurrentRoom==null||SceneManager.GetActiveScene().name!=GameConstants.GAME_SCENE)return;
        var room=n.CurrentRoom;
        GUILayout.BeginArea(PanelRect,GUI.skin.box);
        GUILayout.Label(room.Phase==RoomPhase.Results?"FINAL RESULTS":"Room "+room.Code);
        if(room.Phase==RoomPhase.Loading)GUILayout.Label("Waiting for players to load...");
        else if(room.Phase==RoomPhase.Countdown)GUILayout.Label("Starts in "+Mathf.CeilToInt(n.RemainingSeconds));
        else if(room.Phase==RoomPhase.Playing)GUILayout.Label("Time: "+Mathf.CeilToInt(n.RemainingSeconds));
        foreach(var p in room.Members)
        {
            Color old=GUI.backgroundColor;
            if(p.Id==n.OwnId)GUI.backgroundColor=new Color(0.3f,0.85f,1f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label((p.Id==n.OwnId?"[YOU] ":"")+(p.Rank==0?"-":p.Rank.ToString())+". "+p.Name);
            GUILayout.Label("Score: "+p.Score+(!p.Connected?" (LEFT)":p.Finished?" (FINISHED)":""));
            GUILayout.EndVertical();GUI.backgroundColor=old;
        }
        if(room.Phase==RoomPhase.Playing&&room.Find(n.OwnId)?.Finished==true)GUILayout.Label("No moves left. Waiting for the other players.");
        GUI.enabled=!n.CommandPending;
        if(room.Phase==RoomPhase.Results&&room.Host==n.OwnId&&GUILayout.Button("Return everyone to lobby"))n.ReturnRoom();
        if(GUILayout.Button("Leave room"))n.LeaveRoom();
        GUI.enabled=true;
        if(!string.IsNullOrEmpty(n.LastError))GUILayout.Label(n.LastError);
        GUILayout.EndArea();
    }
}
