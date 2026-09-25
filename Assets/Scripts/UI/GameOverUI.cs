using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI leftTimeText;
    [SerializeField] private TextMeshProUGUI difficultyLevelText;

    [SerializeField] private Button returnToTitleButton;
    [SerializeField] private Button restartButton;

    void Start()
    {
        returnToTitleButton.onClick.AddListener(GameManager.Instance.ReturnToTitle);
        restartButton.onClick.AddListener(GameManager.Instance.RestartGame);
    }

    public void GameOver(int score, float leftTime)
    {
        gameOverPanel.SetActive(true);
        scoreText.text = $"점수: {score}";
        leftTimeText.text = $"남은 시간: {leftTime}";
        ShowDifficultyLevel();
    }

    private void ShowDifficultyLevel()
    {
        int difficultyLevel = GameManager.Instance.GetDifficultyLevel();
        string difficultyText = difficultyLevel switch
        {
            0 => "☆☆☆☆☆",
            1 => "★☆☆☆☆",
            2 => "★★☆☆☆",
            3 => "★★★☆☆",
            4 => "★★★★☆",
            5 => "★★★★★",
            _ => "알 수 없음"
        };
        difficultyLevelText.text = $"난이도: {difficultyText}";
    }
}
