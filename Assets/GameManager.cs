using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
            UIManager.Instance.OnCountdownEnd += OnCountdownEnd;
        }
    }

    // 이벤트 구독 해제 
    private void OnDisable()
    {
        if (dragSelector != null)
        {
            dragSelector.OnDragging -= OnDragging;
            dragSelector.OnDragEnd -= OnDragEnd;
            UIManager.Instance.OnCountdownEnd -= OnCountdownEnd;
        }
    }

    void Start()
    {
        StartCoroutine(UIManager.Instance.StartCountdown(countdownSec)); 
    }

    // 타이머 업데이트 및 게임 종료 체크
    void Update()
    {
        if (!isGameActive || isGameOver) { return; }
        if (score == 170) { GameClear(); }

        currentTime += Time.deltaTime;

        // 남은 시간 0 이하일 경우 게임 종료 처리 
        if (currentTime >= timeLimit)
        {
            currentTime = 0;
            GameOver();
        }

        // 타이머 UI 갱신 
        if (timerImage != null)
        {
            timerImage.fillAmount = (timeLimit - currentTime) / timeLimit; 
        }
    }

    /*private IEnumerator StartCountdown()
    {
        countdownPanel.SetActive(true); 

        countdownText.text = "3";
        yield return new WaitForSeconds(0.5f);

        countdownText.text = "2";
        yield return new WaitForSeconds(0.5f);

        countdownText.text = "1";
        yield return new WaitForSeconds(0.5f);

        isGameActive = true;
        dragSelector.enabled = true;
        countdownPanel.SetActive(false); 
    }*/

    private void OnCountdownEnd()
    {
        isGameActive = true;
        dragSelector.enabled = true;
        UIManager.Instance.countdownPanel.SetActive(false);
        AudioManager.Instance.PlayBGM(); 
    }

    // DragSelector로부터 드래그 중인 범위를 전달받아 실행될 이벤트 핸들러 함수 
    private void OnDragging(Vector2 start, Vector2 current)
    {
       if (isGameOver) { return; }

        // 이전에 선택된 사과들의 하이라이트 제거 
        if (selectedApples != null)
        {
            foreach (Apple apple in selectedApples)
            {
                apple.SetSelected(false);
            }
        }

        // 현재 선택된 사과들 하이라이트 
        selectedApples = boardManager.GetApplesInDraggedArea(start, current);

        if (selectedApples != null)
        {
            foreach (Apple apple in selectedApples)
            {
                apple.SetSelected(true);
            }
        }
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
            foreach (Apple apple in selectedApples)
            {
                apple.SetSelected(false); 
            }
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

    // 게임 종료 처리 (입력 차단, 결과 화면 출력 등) 
    private void GameOver()
    {
        isGameOver = true;
        dragSelector.enabled = false;
        AudioManager.Instance.StopBGM(); 
        Debug.Log($"게임 종료! 최종 점수: {score}"); 
    }

    // 모든 사과를 처리 성공한 경우 Game Clear 
    private void GameClear()
    {
        isGameOver = true;
        dragSelector.enabled = false;
        AudioManager.Instance.StopBGM();
        Debug.Log($"게임 클리어! 남은 시간: {Mathf.CeilToInt(timeLimit - currentTime)}"); 
    }
}
