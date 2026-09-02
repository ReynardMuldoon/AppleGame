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
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private TextMeshProUGUI countdownText; 

    // 게임 상태 관리 변수 
    private int score = 0;
    private float timeLimit = 120f; // 120초 제한 시간
    private float currentTime = 0;
    private bool isGameOver = false;

    private int countdownSec = 3;

    // 카운트다운 UI 동안 실행되지 않도록 하기 위한 변수 
    private bool isGameActive = false; 

    private List<Apple> selectedApples = null; 

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
        UIManager.Instance.StartCountdown(countdownSec, OnCountdownEnd); 
    }

    // 타이머 업데이트 및 게임 종료 체크
    void Update()
    {
        if (!isGameActive || isGameOver) { return; }

        // 사과 판의 크기에 변경이 생기거나 게임 규칙의 변경에 대응할 수 있도록 변경 필요 
        if (score == 170) { GameClear(); } 

        currentTime += Time.deltaTime;

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
        ApplesHighlightOnOff(false);

        // 현재 선택된 사과들 하이라이트 
        selectedApples = boardManager.GetApplesInDraggedArea(start, current);

        ApplesHighlightOnOff(true);
    }

    // DragSelector로부터 드래그 완료된 범위를 전달받아 실행될 이벤트 핸들러 함수 
    private void OnDragEnd(Vector2 start, Vector2 end)
    {
        if (isGameOver || selectedApples == null || selectedApples.Count <= 0) { return; }

        int totalValue = 0;
        foreach (Apple apple in selectedApples)
        {
            totalValue += apple.GetValue();
        }

        // 선택된 사과들의 총합이 10일 경우 점수 추가
        if (totalValue == 10)
        {
            boardManager.RemoveSelectedApples(selectedApples); 
            AddScore(selectedApples.Count); 
        }

        else
        {
            ApplesHighlightOnOff(false);
        }

        Debug.Log($"Selected Apples Count: {selectedApples.Count}, Sum of Values: {totalValue}");
        selectedApples.Clear(); 
    }

    // 점수 추가 및 UI 업데이트
    private void AddScore(int points)
    {
        score += points;
        if (scoreText != null)
        {
            scoreText.text = score.ToString();
        }
    }

    private void ApplesHighlightOnOff(bool onOff)
    {
        if (selectedApples != null)
        {
            foreach (Apple apple in selectedApples) 
            {
                apple.SetSelected(onOff);
            }
        }
    } 

    // 게임 종료 처리 (입력 차단, 결과 화면 출력 등) 
    private void GameOver()
    {
        isGameOver = true;
        dragSelector.enabled = false;
        // 이전에 선택된 사과들의 하이라이트 제거 
        ApplesHighlightOnOff(false);
        AudioManager.Instance.StopBGM();
        UIManager.Instance.GameEnd(score, timeLimit - currentTime, false); 
        Debug.Log($"게임 종료! 최종 점수: {score}");
        UpdateBestScore();
    }

    // 모든 사과를 처리 성공한 경우 Game Clear 
    private void GameClear()
    {
        isGameOver = true;
        dragSelector.enabled = false;
        // 이전에 선택된 사과들의 하이라이트 제거 
        ApplesHighlightOnOff(false); 
        AudioManager.Instance.StopBGM();
        UIManager.Instance.GameEnd(score, timeLimit - currentTime, true); 
        Debug.Log($"게임 클리어! 남은 시간: {(timeLimit - currentTime)}");
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
}
