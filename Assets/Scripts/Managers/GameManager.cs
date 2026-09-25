using System;
using System.Collections.Generic;
using AppleNet;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임 씬에서 보드 생성, 입력, 점수 표시와 종료 흐름을 조정한다.
/// 싱글플레이는 로컬에서 선택과 시간을 판정하고, 멀티플레이는 서버 결과를 반영한다.
/// 실제 통신은 NetworkManager, 사과 객체와 숫자 배열 관리는 BoardManager에 맡긴다.
/// 씬에 속하는 객체이며 NetworkManager처럼 씬 전환 후에도 유지되는 객체는 아니다.
/// Unity 생명주기 및 드래그/선택 결과 콜백은 메인 스레드에서 처리하는 것을 전제로 한다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance
    {
        get; private set;
    }

    // Inspector에서 연결한다. 보드와 입력 컴포넌트가 없으면 초기화를 중단한다.
    [SerializeField] private BoardManager boardManager;
    [SerializeField] private DragSelector dragSelector;
    [SerializeField] private TextMeshPro scoreText;
    [SerializeField] private Image timerImage;

    // 싱글에서는 직접 누적하고, 멀티에서는 서버가 전달한 내 점수를 저장한다.
    private int score;

    // 싱글 제한 시간 및 멀티 시간 게이지의 기준(초).
    // 멀티의 실제 종료 시점은 이 상수가 아니라 서버의 게임 상태로 결정된다.
    private const float TimeLimit = 120f;

    // 싱글플레이 전용 경과 시간과 다음 힌트 계산까지 누적한 시간(초).
    private float currentTime, hintTimer;

    // active: 현재 플레이 가능한 상태. 멀티에서는 방 상태와 개인 종료 여부로 갱신한다.
    // ended: 싱글 종료 또는 멀티 전체 결과 단계에 도달했는지 여부.
    // musicStarted: BGM 시작을 이미 시도했는지 여부이며 실제 재생 여부와는 다르다.
    // multi: Start에서 결정한 이 씬의 모드. initialized: 보드 생성까지 완료했는지 여부.
    private bool active, ended, musicStarted, multi, initialized;

    // 드래그로 선택한 사과와 현재 힌트 사과의 1차원 인덱스 목록.
    // 초기에는 null이며 BoardManager의 강조/힌트 함수는 null 목록을 허용한다.
    private List<int> selected, hinted;
    private NetworkManager network;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 보드 준비와 카운트다운이 끝나기 전에는 드래그 입력을 받지 않는다.
        if (dragSelector != null)
            dragSelector.enabled = false;
    }

    // 컴포넌트 활성화/비활성화에 맞추어 입력 이벤트 구독을 대칭으로 관리한다.
    private void OnEnable()
    {
        if (dragSelector != null)
        {
            dragSelector.OnDragging += OnDragging;
            dragSelector.OnDragEnd += OnDragEnd;
        }
    }

    private void OnDisable()
    {
        if (dragSelector != null)
        {
            dragSelector.OnDragging -= OnDragging;
            dragSelector.OnDragEnd -= OnDragEnd;
        }
    }

    /// <summary>
    /// 모드에 맞는 초기 보드를 준비한다.
    /// 멀티는 서버 Prepare 데이터가 필요하며, 싱글은 서버 없이 숫자를 생성한다.
    /// </summary>
    private void Start()
    {
        network = NetworkManager.Ensure();
        multi = network.Multiplayer;
        if (boardManager == null || dragSelector == null)
        {
            Debug.LogError("BoardManager/DragSelector references are missing.");
            return;
        }
        if (multi)
        {
            // 준비 패킷 없이 멀티 게임 씬에 들어오면 진행할 보드가 없으므로 연결을 정리한다.
            if (network.Prepared == null)
            {
                network.Disconnect();
                SceneManager.LoadScene(GameConstants.TITLE_SCENE);
                return;
            }

            // 이 이벤트는 NetworkManager의 수신 큐 처리 중 메인 스레드에서 호출된다.
            // NetworkManager가 더 오래 살아 있으므로 OnDestroy에서 구독을 해제한다.
            network.SelectionReceived += ApplyServerSelection;
            boardManager.GenerateBoard(network.Prepared.Board);
            initialized = true;

            // 로컬 보드 준비 완료를 서버에 알린다. 실제 시작은 서버의 Playing 상태를 기다린다.
            network.Loaded();
        }
        else
        {
            boardManager.GenerateBoard(LocalBoardRules.Generate(new System.Random()));
            initialized = true;

            // 싱글 카운트다운은 UIManager가 끝난 뒤 콜백으로 게임을 시작한다.
            // UIManager가 없을 때는 즉시 시작하는 대체 경로를 사용한다.
            if (UIManager.Instance != null)
                UIManager.Instance.StartCountdown(3, StartSinglePlay);
            else
                StartSinglePlay();
        }
        SetScore(0);
    }

    // 싱글 카운트다운 완료 콜백. 초기 보드에 가능한 선택이 없으면 바로 종료한다.
    private void StartSinglePlay()
    {
        if (ended)
            return;
        active = true;
        StartMusic();
        if (!AppleGameSolver.HasAvailableMoves(boardManager.GetAppleArray()))
            FinishSingle();
    }

    private void StartMusic()
    {
        // 프레임마다 호출되어도 재생 위치를 매번 처음으로 되돌리지 않도록 한 번만 요청한다.
        // 오디오 매니저가 없는 경우에도 플래그는 true가 되어 이 씬에서는 재시도하지 않는다.
        if (!musicStarted)
        {
            musicStarted = true;
            AudioManager.Instance?.PlayBGM();
        }
    }

    private void Update()
    {
        if (!initialized)
            return;
        if (multi)
        {
            FitMultiplayerCamera();
            var room = network.CurrentRoom;
            var me = room?.Find(network.OwnId);

            // 개인 종료와 방 전체 종료를 구분한다. 개인이 먼저 끝나면 입력만 중단하고
            // 방 상태와 순위는 계속 수신한다. 점수가 0인지 여부는 종료 조건이 아니다.
            active = room != null && room.Phase == RoomPhase.Playing && me != null && !me.Finished;
            if (active)
                StartMusic();
            if (room != null && room.Phase == RoomPhase.Results)
            {
                ended = true;
                // 현재 구현은 Results 단계의 매 프레임마다 정지를 요청한다.
                AudioManager.Instance?.StopBGM();
            }
            if (me != null)
                SetScore((int)me.Score);
            if (timerImage != null)
                // 시간은 표시용 추정치이다. 0이 되어도 여기서 전체 게임 종료를 판정하지 않는다.
                timerImage.fillAmount = room != null && room.Phase == RoomPhase.Results ? 0 : Mathf.Clamp01(network.RemainingSeconds / TimeLimit);

            // 서버 선택 응답을 기다리는 동안에는 새 드래그를 받지 않는다.
            dragSelector.enabled = active && !network.SelectionPending && !ended;

            // 멀티에서는 아래의 싱글 타이머, 힌트 및 로컬 종료 판정을 실행하지 않는다.
            // 전체 결과 UI는 별도의 MultiplayerHud에서 방 상태를 바탕으로 표시한다.
            return;
        }
        dragSelector.enabled = active && !ended;
        if (!active || ended)
            return;

        // 싱글은 Time.deltaTime으로 진행하므로 timeScale의 영향을 받는다.
        // 카운트다운 중에는 active가 false여서 게임 시간이 흐르지 않는다.
        currentTime = Mathf.Min(TimeLimit, currentTime + Time.deltaTime);
        hintTimer += Time.deltaTime;
        if (timerImage != null)
            timerImage.fillAmount = (TimeLimit - currentTime) / TimeLimit;
        if (currentTime >= TimeLimit)
        {
            FinishSingle();
            return;
        }
        if (hintTimer >= 5f)
        {
            // 기존 힌트를 지우고 현재 보드에서 새 영역을 찾는다.
            // 성공적으로 사과를 제거하면 ClearHint에서 이 주기를 다시 시작한다.
            hintTimer = 0;
            boardManager.HintApples(hinted, false);
            var rect = AppleGameSolver.GetHint(boardManager.GetAppleArray());
            if (rect.isValid)
            {
                // 솔버 영역의 끝 행/열을 포함하는 GetApplesByIndex를 사용한다.
                hinted = boardManager.GetApplesByIndex(rect.r1, rect.r2, rect.c1, rect.c2);
                boardManager.HintApples(hinted, true);
            }
        }
    }

    /// <summary>
    /// 우측 순위 패널에 255픽셀을 남기고 나머지 영역에 게임 카메라를 배치한다.
    /// 매 프레임 계산하여 창 크기 변경에 대응한다. 기본 보드 배치/배율을 전제로 한 계산이다.
    /// </summary>
    private void FitMultiplayerCamera()
    {
        var cam = Camera.main;
        if (cam == null)
            return;
        float width = Mathf.Max(1, Screen.width - 255);

        // Camera.rect는 픽셀이 아닌 화면 비율(0~1)을 사용한다.
        cam.rect = new Rect(0, 0, width / Screen.width, 1);
        if (cam.orthographic)
            // orthographicSize는 세로 절반 길이이다. 보드 폭과 화면 종횡비로 필요한 값을 구하고,
            // 현재 기본 보드의 세로 공간을 확보하도록 최소값을 5로 둔다.
            cam.orthographicSize = Mathf.Max(5f, (GameConstants.COLUMN * BoardManager.spacer + 1f) / (2f * (width / Screen.height)));
    }

    // 드래그 도중에는 선택 표시만 갱신한다. 사과 제거 요청은 드래그 종료 시 한 번 보낸다.
    private void OnDragging(Vector2 start, Vector2 current)
    {
        if (!active || ended || (multi && network.SelectionPending))
            return;
        boardManager.HighlightApples(selected, false);
        selected = boardManager.GetApplesInDraggedArea(start, current);
        boardManager.HighlightApples(selected, true);
    }

    /// <summary>
    /// 월드 좌표의 드래그 종료 지점으로 선택을 확정한다.
    /// 싱글은 합을 로컬에서 검사하고, 멀티는 영역을 전송한 뒤 서버 응답을 기다린다.
    /// </summary>
    private void OnDragEnd(Vector2 start, Vector2 end)
    {
        if (!active || ended || (multi && network.SelectionPending))
            return;

        // 마지막 OnDragging 이후 마우스가 이동했을 수 있으므로 실제 놓은 위치로 다시 계산한다.
        boardManager.HighlightApples(selected, false);
        selected = boardManager.GetApplesInDraggedArea(start, end);
        if (selected.Count == 0)
            return;
        if (multi)
        {
            // 서버와 같은 끝 제외 범위 [r0, r1) × [c0, c1)로 전송한다.
            // 사과 인덱스 목록 대신 사각형을 보내며, 빈칸과 숫자의 판정은 서버가 수행한다.
            boardManager.GetSelectionArea(start, end, out int r0, out int c0, out int r1, out int c1);
            if (network.Select(r0, c0, r1, c1))
            {
                // true는 요청을 송신 큐에 넣었다는 뜻이다. 아직 사과를 제거하거나 점수를 올리지 않는다.
                boardManager.HighlightApples(selected, true);
                dragSelector.enabled = false;
            }
            return;
        }
        int sum = 0;
        foreach (int i in selected)
            sum += boardManager.GetAppleArray()[i];
        if (sum != 10)
        {
            selected.Clear();
            return;
        }

        // 선택 목록에는 남아 있는 사과만 들어 있으므로 개수가 곧 이번 획득 점수이다.
        int removed = selected.Count;
        boardManager.RemoveSelectedApples(selected);
        SetScore(score + removed);
        selected.Clear();
        ClearHint();
        if (!AppleGameSolver.HasAvailableMoves(boardManager.GetAppleArray()))
            FinishSingle();
    }

    /// <summary>
    /// NetworkManager가 현재 대기 요청과 일치한다고 확인한 선택 응답을 반영한다.
    /// 성공/실패 모두 서버 보드와 점수를 기준으로 처리하며 현재 마우스 위치로 재판정하지 않는다.
    /// </summary>
    private void ApplyServerSelection(SelectionResult result)
    {
        if (!multi || !initialized)
            return;
        boardManager.HighlightApples(selected, false);
        selected?.Clear();

        // 응답 대기 동안 입력 상태가 바뀌더라도 제거 대상은 서버 스냅샷으로 결정한다.
        // BoardManager는 기존 값 유지 또는 사과 제거만 허용하고 예상 밖 변화에는 예외를 발생시킨다.
        boardManager.ApplySnapshot(result.Board);
        SetScore((int)result.Score);
        if (result.Finished)
        {
            // 개인 종료: 전체 결과 화면을 띄우지 않고 서버의 방 상태를 계속 기다린다.
            active = false;
            dragSelector.enabled = false;
        }
    }

    // 힌트 표시와 재계산 주기를 함께 초기화한다.
    private void ClearHint()
    {
        hintTimer = 0;
        boardManager.HintApples(hinted, false);
        hinted?.Clear();
    }

    // 내부 점수와 화면을 함께 갱신한다. 점수의 유효성 판단은 호출 경로에서 담당한다.
    private void SetScore(int value)
    {
        score = value;
        if (scoreText != null)
            scoreText.text = score.ToString();
    }

    /// <summary>
    /// 싱글플레이의 시간 만료 또는 가능한 선택 없음에 따른 종료 처리.
    /// 멀티플레이 결과에는 사용하지 않으며, 최고 기록도 이 싱글 종료 경로에서만 저장한다.
    /// </summary>
    private void FinishSingle()
    {
        // 종료 조건이 연이어 발생해도 결과 표시와 기록 저장을 중복 실행하지 않는다.
        if (ended)
            return;
        ended = true;
        active = false;
        dragSelector.enabled = false;
        boardManager.HighlightApples(selected, false);
        ClearHint();
        AudioManager.Instance?.StopBGM();

        // 두 번째 인수는 경과 시간이 아니라 남은 시간이다.
        UIManager.Instance?.GameEnd(score, TimeLimit - currentTime);
        if (score > PlayerPrefs.GetInt(GameConstants.BEST_SCORE_KEY, 0))
        {
            PlayerPrefs.SetInt(GameConstants.BEST_SCORE_KEY, score);
            PlayerPrefs.Save();
        }
    }

    // 초기 보드 생성 시 계산한 난이도를 UI에 제공한다. 참조가 없으면 0을 반환한다.
    public int GetDifficultyLevel()
    {
        return boardManager != null ? boardManager.difficultyLevel : 0;
    }

    /// <summary>
    /// 싱글은 즉시 타이틀로 이동한다. 멀티는 퇴장을 요청하고 서버 응답에 따른 씬 전환을 기다린다.
    /// </summary>
    public void ReturnToTitle()
    {
        if (multi)
        {
            network.LeaveRoom();
            return;
        }
        SceneManager.LoadScene(GameConstants.TITLE_SCENE);
    }

    /// <summary>
    /// 싱글은 게임 씬을 다시 시작한다. 멀티는 방장만 전체 대기실 복귀를 요청한다.
    /// 멀티에서 이 함수는 즉시 새 게임을 시작하는 동작이 아니며 서버가 단계와 권한을 검증한다.
    /// </summary>
    public void RestartGame()
    {
        if (multi)
        {
            if (network.CurrentRoom?.Host == network.OwnId)
                network.ReturnRoom();
            return;
        }
        network.StartSingle();
    }

    private void OnDestroy()
    {
        // 씬보다 오래 유지되는 NetworkManager가 파괴된 GameManager를 호출하지 않도록 해제한다.
        if (network != null)
            network.SelectionReceived -= ApplyServerSelection;
        if (Instance == this)
            Instance = null;

        // 씬 파괴 중에는 AudioSource가 이미 파괴되었을 수 있어 여기서 StopBGM을 호출하지 않는다.
        // 정상적인 게임 종료의 BGM 정지는 위의 싱글/멀티 종료 처리에서 수행한다.
    }
}
