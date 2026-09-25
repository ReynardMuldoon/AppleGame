using AppleNet;
using UnityEngine;
using UnityEngine.SceneManagement;

// 멀티플레이 상태를 표시하는 기능 확인용 IMGUI 패널. 판정과 순위 계산은 하지 않는다.
// OnGUI는 한 프레임에 여러 이벤트로 호출될 수 있으므로 여기서 타이머를 누적하지 않는다.
public class MultiplayerHud : MonoBehaviour
{
    // GUI 좌표(좌상단 원점)에서 오른쪽 255픽셀을 차지한다.
    // 검토: 폭 255와 GameManager 카메라의 예약 폭이 일치해야 한다.
    // 작은 화면/많은 참가자에는 내용이 잘릴 수 있어 최종 UI에는 크기 대응과 스크롤이 필요하다.
    public static Rect PanelRect
    {
        get
        {
            return new Rect(Mathf.Max(0, Screen.width - 255), 0, 255, Screen.height);
        }
    }
    // 입력의 좌하단 원점 y를 GUI의 좌상단 원점 y로 변환하여 패널 위인지 확인한다.
    // 검토: OnGUI와 달리 현재 씬/방 존재 여부는 검사하지 않는다. 다른 씬에서 호출할 경우
    // 보이지 않는 패널 영역까지 입력을 차단할 수 있으므로 호출 범위를 확인해야 한다.
    public static bool BlocksPointer(Vector2 screenPosition)
    {
        var n = NetworkManager.Instance;
        return n != null && n.Multiplayer && PanelRect.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));
    }
    // 현재 방 상태를 즉시 읽어 표시한다. 시간 값은 표시용이며 서버 종료를 대신 판정하지 않는다.
    private void OnGUI()
    {
        var n = NetworkManager.Instance;
        if (n == null || !n.Multiplayer || n.CurrentRoom == null || SceneManager.GetActiveScene().name != GameConstants.GAME_SCENE)
            return;
        var room = n.CurrentRoom;
        GUILayout.BeginArea(PanelRect, GUI.skin.box);
        GUILayout.Label(room.Phase == RoomPhase.Results ? "FINAL RESULTS" : "Room " + room.Code);
        if (room.Phase == RoomPhase.Loading)
            GUILayout.Label("Waiting for players to load...");
        else if (room.Phase == RoomPhase.Countdown)
            GUILayout.Label("Starts in " + Mathf.CeilToInt(n.RemainingSeconds));
        else if (room.Phase == RoomPhase.Playing)
            GUILayout.Label("Time: " + Mathf.CeilToInt(n.RemainingSeconds));
        // 서버에서 받은 목록 순서와 Rank를 그대로 표시한다. 클라이언트에서 재정렬하지 않는다.
        foreach (var p in room.Members)
        {
            // 본인 배경 강조가 다음 참가자에게 번지지 않도록 원래 색상을 저장하고 복원한다.
            Color old = GUI.backgroundColor;
            if (p.Id == n.OwnId)
                GUI.backgroundColor = new Color(0.3f, 0.85f, 1f);
            GUILayout.BeginVertical(GUI.skin.box);
            // 0등을 출력하지 않고 -로 표시한다. [YOU]는 본인 위치 확인용 표시이다.
            GUILayout.Label((p.Id == n.OwnId ? "[YOU] " : "") + (p.Rank == 0 ? "-" : p.Rank.ToString()) + ". " + p.Name);
            // 연결 종료 표시가 개인 종료 표시보다 우선한다.
            GUILayout.Label("Score: " + p.Score + (!p.Connected ? " (LEFT)" : p.Finished ? " (FINISHED)" : ""));
            GUILayout.EndVertical();
            GUI.backgroundColor = old;
        }
        // 개인이 먼저 끝나도 방 전체는 진행 중일 수 있으므로 대기 안내를 표시한다.
        // 검토: Finished에는 종료 사유가 없는데 문구는 가능한 선택 없음으로 고정되어 있다.
        // 종료 사유가 늘어나면 별도 사유 코드 또는 중립적인 문구가 필요하다.
        if (room.Phase == RoomPhase.Playing && room.Find(n.OwnId)?.Finished == true)
            GUILayout.Label("No moves left. Waiting for the other players.");
        // 일반 요청 응답을 기다리는 동안 버튼 중복 클릭을 막는다. 서버도 권한/상태를 검사한다.
        // 검토: 아래에서 true로 고정 복원하므로 외부 GUI.enabled 상태를 보존하지 않는다.
        // 다른 GUI와 조합할 때는 진입 전 값을 저장하고 복원하는 방식을 고려한다.
        GUI.enabled = !n.CommandPending;
        // 결과 화면의 전체 대기실 복귀는 방장에게만 제공한다. 바로 새 게임을 시작하지는 않는다.
        if (room.Phase == RoomPhase.Results && room.Host == n.OwnId && GUILayout.Button("Return everyone to lobby"))
            n.ReturnRoom();
        // 퇴장은 요청만 보낸다. 실제 상태 정리와 씬 전환은 서버 응답 처리에서 수행한다.
        if (GUILayout.Button("Leave room"))
            n.LeaveRoom();
        GUI.enabled = true;
        if (!string.IsNullOrEmpty(n.LastError))
            GUILayout.Label(n.LastError);
        GUILayout.EndArea();
    }
}
