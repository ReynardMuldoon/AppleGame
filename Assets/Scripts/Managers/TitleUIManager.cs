using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleUIManager : MonoBehaviour
{
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button exitButton; 

    void Start()
    {
        if (gameStartButton != null)
        {
            gameStartButton.onClick.AddListener(GameStart); 
        }

        if (exitButton != null)
        {
            exitButton.onClick.AddListener(Exit);
        }
    }

    private void GameStart()
    {
        SceneManager.LoadScene(GameConstants.GAME_SCENE); 
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
