using System;
using System.Collections.Generic;
using AppleNet;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance {get;private set;}
    [SerializeField] private BoardManager boardManager;
    [SerializeField] private DragSelector dragSelector;
    [SerializeField] private TextMeshPro scoreText;
    [SerializeField] private Image timerImage;
    private int score;private const float TimeLimit=120f;
    private float currentTime,hintTimer;
    private bool active,ended,musicStarted,multi,initialized;
    private List<int> selected,hinted;
    private NetworkManager network;
    private void Awake()
    {
        if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;
        if(dragSelector!=null)dragSelector.enabled=false;
    }
    private void OnEnable()
    {
        if(dragSelector!=null){dragSelector.OnDragging+=OnDragging;dragSelector.OnDragEnd+=OnDragEnd;}
    }
    private void OnDisable()
    {
        if(dragSelector!=null){dragSelector.OnDragging-=OnDragging;dragSelector.OnDragEnd-=OnDragEnd;}
    }
    private void Start()
    {
        network=NetworkManager.Ensure();multi=network.Multiplayer;
        if(boardManager==null||dragSelector==null){Debug.LogError("BoardManager/DragSelector references are missing.");return;}
        if(multi)
        {
            if(network.Prepared==null){network.Disconnect();SceneManager.LoadScene(GameConstants.TITLE_SCENE);return;}
            network.SelectionReceived+=ApplyServerSelection;
            boardManager.GenerateBoard(network.Prepared.Board);initialized=true;network.Loaded();
        }
        else
        {
            boardManager.GenerateBoard(LocalBoardRules.Generate(new System.Random()));initialized=true;
            if(UIManager.Instance!=null)UIManager.Instance.StartCountdown(3,StartSinglePlay);
            else StartSinglePlay();
        }
        SetScore(0);
    }
    private void StartSinglePlay()
    {
        if(ended)return;active=true;StartMusic();
        if(!AppleGameSolver.HasAvailableMoves(boardManager.GetAppleArray()))FinishSingle();
    }
    private void StartMusic(){if(!musicStarted){musicStarted=true;AudioManager.Instance?.PlayBGM();}}
    private void Update()
    {
        if(!initialized)return;
        if(multi)
        {
            FitMultiplayerCamera();
            var room=network.CurrentRoom;var me=room?.Find(network.OwnId);
            active=room!=null&&room.Phase==RoomPhase.Playing&&me!=null&&!me.Finished;
            if(active)StartMusic();
            if(room!=null&&room.Phase==RoomPhase.Results){ended=true;AudioManager.Instance?.StopBGM();}
            if(me!=null)SetScore((int)me.Score);
            if(timerImage!=null)timerImage.fillAmount=room!=null&&room.Phase==RoomPhase.Results?0:Mathf.Clamp01(network.RemainingSeconds/TimeLimit);
            dragSelector.enabled=active&&!network.SelectionPending&&!ended;
            return; // Multiplayer has no client hint timer and no client end authority.
        }
        dragSelector.enabled=active&&!ended;
        if(!active||ended)return;
        currentTime=Mathf.Min(TimeLimit,currentTime+Time.deltaTime);hintTimer+=Time.deltaTime;
        if(timerImage!=null)timerImage.fillAmount=(TimeLimit-currentTime)/TimeLimit;
        if(currentTime>=TimeLimit){FinishSingle();return;}
        if(hintTimer>=5f)
        {
            hintTimer=0;boardManager.HintApples(hinted,false);
            var rect=AppleGameSolver.GetHint(boardManager.GetAppleArray());
            if(rect.isValid){hinted=boardManager.GetApplesByIndex(rect.r1,rect.r2,rect.c1,rect.c2);boardManager.HintApples(hinted,true);}
        }
    }
    private void FitMultiplayerCamera()
    {
        var cam=Camera.main;if(cam==null)return;
        float width=Mathf.Max(1,Screen.width-255);
        cam.rect=new Rect(0,0,width/Screen.width,1);
        if(cam.orthographic)
            cam.orthographicSize=Mathf.Max(5f,(GameConstants.COLUMN*BoardManager.spacer+1f)/(2f*(width/Screen.height)));
    }
    private void OnDragging(Vector2 start,Vector2 current)
    {
        if(!active||ended||(multi&&network.SelectionPending))return;
        boardManager.HighlightApples(selected,false);selected=boardManager.GetApplesInDraggedArea(start,current);
        boardManager.HighlightApples(selected,true);
    }
    private void OnDragEnd(Vector2 start,Vector2 end)
    {
        if(!active||ended||(multi&&network.SelectionPending))return;
        // Recompute from the release position; do not reuse the previous frame's selection.
        boardManager.HighlightApples(selected,false);selected=boardManager.GetApplesInDraggedArea(start,end);
        if(selected.Count==0)return;
        if(multi)
        {
            boardManager.GetSelectionArea(start,end,out int r0,out int c0,out int r1,out int c1);
            if(network.Select(r0,c0,r1,c1)){boardManager.HighlightApples(selected,true);dragSelector.enabled=false;}
            return;
        }
        int sum=0;foreach(int i in selected)sum+=boardManager.GetAppleArray()[i];
        if(sum!=10){selected.Clear();return;}
        int removed=selected.Count;boardManager.RemoveSelectedApples(selected);SetScore(score+removed);
        selected.Clear();ClearHint();
        if(!AppleGameSolver.HasAvailableMoves(boardManager.GetAppleArray()))FinishSingle();
    }
    private void ApplyServerSelection(SelectionResult result)
    {
        if(!multi||!initialized)return;
        boardManager.HighlightApples(selected,false);selected?.Clear();
        // Apply the server's board snapshot, never the current mouse selection.
        boardManager.ApplySnapshot(result.Board);SetScore((int)result.Score);
        if(result.Finished){active=false;dragSelector.enabled=false;}
    }
    private void ClearHint(){hintTimer=0;boardManager.HintApples(hinted,false);hinted?.Clear();}
    private void SetScore(int value){score=value;if(scoreText!=null)scoreText.text=score.ToString();}
    private void FinishSingle()
    {
        if(ended)return;ended=true;active=false;dragSelector.enabled=false;
        boardManager.HighlightApples(selected,false);ClearHint();AudioManager.Instance?.StopBGM();
        UIManager.Instance?.GameEnd(score,TimeLimit-currentTime);
        if(score>PlayerPrefs.GetInt(GameConstants.BEST_SCORE_KEY,0)){PlayerPrefs.SetInt(GameConstants.BEST_SCORE_KEY,score);PlayerPrefs.Save();}
    }
    public int GetDifficultyLevel(){return boardManager!=null?boardManager.difficultyLevel:0;}
    public void ReturnToTitle()
    {
        if(multi){network.LeaveRoom();return;}SceneManager.LoadScene(GameConstants.TITLE_SCENE);
    }
    public void RestartGame()
    {
        if(multi){if(network.CurrentRoom?.Host==network.OwnId)network.ReturnRoom();return;}
        network.StartSingle();
    }
    private void OnDestroy()
    {
        if(network!=null)network.SelectionReceived-=ApplyServerSelection;
        if(Instance==this)Instance=null;
    }
}
