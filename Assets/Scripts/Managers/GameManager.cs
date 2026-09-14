using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // 중재자로서 연결할 외부 스크립트들 
    [SerializeField] private BoardManager boardManager;
    [SerializeField] private DragSelector dragSelector;

    // UI 컴포넌트 연결 
    [SerializeField] private TextMeshPro scoreText;
    [SerializeField] private Image timerImage;

    // 게임 상태 관리 변수 
    private int score = 0;
    private float timeLimit = 120f; // 120초 제한 시간
    private float currentTime = 0;
    private bool isGameOver = false;

    private int countdownSec = 3;

    private float hintTimer = 0f;
    private float hintInterval = 5f; 

    // 카운트다운 UI 동안 실행되지 않도록 하기 위한 변수 
    private bool isGameActive = false; 

    private List<int> selectedAppleIndices = null;
    private List<int> hintedAppleIndices = null;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this; 
        }

        else
        {
            Destroy(gameObject); 
        }
    }

    // 이벤트 구독 등록 
    private void OnEnable()
    {
        if (dragSelector != null)
        {
            dragSelector.OnDragging += OnDragging;
            dragSelector.OnDragEnd += OnDragEnd;
        }
    }

    // 이벤트 구독 해제 
    private void OnDisable()
    {
        if (dragSelector != null)
        {
            dragSelector.OnDragging -= OnDragging;
            dragSelector.OnDragEnd -= OnDragEnd;
        }
    }

    void Start()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnGameStartReceived += HandleGameStartData;
            NetworkManager.Instance.OnDragResultReceived += HandleDragResult;

            NetworkManager.Instance.SendGameStartRequest();
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnGameStartReceived -= HandleGameStartData;
            NetworkManager.Instance.OnDragResultReceived -= HandleDragResult;
        }
    }

    // 타이머 업데이트 및 게임 종료 체크
    void Update()
    {
        if (!isGameActive || isGameOver) { return; }

        currentTime += Time.deltaTime;
        hintTimer += Time.deltaTime;

        // 남은 시간 0 이하일 경우 게임 종료 처리 
        if (currentTime >= timeLimit)
        {
            currentTime = timeLimit;
            GameOver();
        }

        // 타이머 UI 갱신 
        if (timerImage != null)
        {
            timerImage.fillAmount = (timeLimit - currentTime) / timeLimit; 
        }

        if (hintTimer >= hintInterval)
        {
            hintTimer = 0f;

            AppleGameSolver.RectData bestRect = AppleGameSolver.GetHint(boardManager.GetAppleArray());
            if (bestRect.isValid)
            {
                hintedAppleIndices = boardManager.GetApplesByIndex(bestRect.r1, bestRect.r2, bestRect.c1, bestRect.c2);
                boardManager.HintApples(hintedAppleIndices, true);
            }
        }
    }

    public void ReturnToTitle()
    {
        SceneManager.LoadScene(GameConstants.TITLE_SCENE); 
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(GameConstants.GAME_SCENE); 
    }

    public int GetDifficultyLevel()
    {
        return boardManager.difficultyLevel;
    }

    private void OnCountdownEnd()
    {
        isGameActive = true;
        dragSelector.enabled = true;
        AudioManager.Instance.PlayBGM(); 
    }

    // DragSelector로부터 드래그 중인 범위를 전달받아 실행될 이벤트 핸들러 함수 
    private void OnDragging(Vector2 start, Vector2 current)
    {
       if (isGameOver) { return; }

        // 이전에 선택된 사과들의 하이라이트 제거 
        boardManager.HighlightApples(selectedAppleIndices, false); 

        // 현재 선택된 사과들 하이라이트 
        selectedAppleIndices = boardManager.GetApplesInDraggedArea(start, current); 
        boardManager.HighlightApples(selectedAppleIndices, true); 
    }

    // DragSelector로부터 드래그 완료된 범위를 전달받아 실행될 이벤트 핸들러 함수 
    private void OnDragEnd(Vector2 start, Vector2 end)
    {
        if (isGameOver || selectedAppleIndices == null || selectedAppleIndices.Count <= 0) { return; }

        NetworkManager.Instance.SendDragAppleRequest(selectedAppleIndices); 
    }

    // 점수 추가 및 UI 업데이트
    private void SetScore(int points)
    {
        score = points;
        if (scoreText != null)
        {
            scoreText.text = score.ToString();
        }
    }

    // 게임 종료 처리 (입력 차단, 결과 화면 출력 등) 
    private void GameOver()
    {
        isGameOver = true;
        dragSelector.enabled = false;
        // 이전에 선택된 사과들의 하이라이트 제거 
        boardManager.HighlightApples(selectedAppleIndices, false);
        AudioManager.Instance.StopBGM();
        UIManager.Instance.GameEnd(score, timeLimit - currentTime); 
        Debug.Log($"게임 종료! 최종 점수: {score}, 남은 시간: {(timeLimit - currentTime)}");
        UpdateBestScore();
    }

    private void UpdateBestScore()
    {
        int currentBestScore = PlayerPrefs.GetInt(GameConstants.BEST_SCORE_KEY, 0);
        if (score > currentBestScore)
        {
            PlayerPrefs.SetInt(GameConstants.BEST_SCORE_KEY, score);
            PlayerPrefs.Save();
            Debug.Log($"새로운 최고 점수 기록: {score}");
        }
    }

    private void HandleGameStartData(byte[] data)
    {
        // 서버로부터 게임 시작 데이터를 수신했을 때 처리할 로직
        boardManager.GenerateBoard(data);
        Debug.Log("게임 시작 데이터 수신 완료");

        UIManager.Instance.StartCountdown(countdownSec, OnCountdownEnd);
    }

    private void HandleDragResult(bool isSuccess, int score)
    {
        // 서버로부터 드래그 결과 데이터를 수신했을 때 처리할 로직
        Debug.Log($"드래그 결과 수신: 성공 여부 - {isSuccess}, 점수 - {score}");
        // 선택된 사과들의 총합이 10일 경우 점수 추가
        if (isSuccess)
        {
            hintTimer = 0;
            boardManager.RemoveSelectedApples(selectedAppleIndices); 
            SetScore(score);

            // 이전 힌트 사과들의 하이라이트 제거
            if (hintedAppleIndices != null)
            {
                boardManager.HintApples(hintedAppleIndices, false);
                hintedAppleIndices.Clear();
            }

            // 사과 제거 이후 더 제거 가능한 사과가 있는지 확인 
            bool hasAvailableMoves = AppleGameSolver.HasAvailableMoves(boardManager.GetAppleArray());
            // 더 제거할 사과가 없다면 즉시 게임 종료 (게임 클리어 처리, 점수 및 남은 시간 출력) 
            if (!hasAvailableMoves)
            {
                GameOver();
            }
        }

        else
        {
            boardManager.HighlightApples(selectedAppleIndices, false);
        }

        selectedAppleIndices.Clear();
    }
}
