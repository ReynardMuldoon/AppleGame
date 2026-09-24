using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AppleNet;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance {get;private set;}
    public static NetworkManager Ensure()
    {
        if(Instance==null)new GameObject("NetworkManager").AddComponent<NetworkManager>();
        return Instance;
    }
    public bool Multiplayer {get;private set;}
    public bool IsConnected {get{return transport!=null&&!transport.Closed;}}
    public bool IsConnecting {get;private set;}
    public bool Handshaken {get{return IsConnected&&OwnId!=0;}}
    public uint OwnId {get;private set;}
    public RoomInfo CurrentRoom {get;private set;}
    public GameData Prepared {get;private set;}
    public RoomPhase ClockPhase {get;private set;}
    public string LastError {get;private set;}="";
    public bool CommandPending {get;private set;}
    public bool SelectionPending {get;private set;}
    public event Action<SelectionResult> SelectionReceived;
    private TcpTransport transport;
    private int attempt; private uint requestId,revision,pendingRequest;
    private float commandTime,selectionTime,clockReceived;
    private uint clockRemaining;
    public float RemainingSeconds {get{return Mathf.Max(0,clockRemaining/1000f-(Time.realtimeSinceStartup-clockReceived));}}
    private void Awake()
    {
        if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}
        Instance=this;DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        if(GetComponent<MultiplayerHud>()==null)gameObject.AddComponent<MultiplayerHud>();
    }
    public void StartSingle()
    {
        Disconnect();Multiplayer=false;LastError="";SceneManager.LoadScene(GameConstants.GAME_SCENE);
    }
    public async Task<bool> ConnectAsync(string host,int port,string nickname)
    {
        if(IsConnecting||IsConnected)return false;
        nickname=nickname.Trim();
        if(nickname.Length==0||Encoding.UTF8.GetByteCount(nickname)>48){LastError="Nickname must be 1-48 UTF-8 bytes.";return false;}
        IsConnecting=true;LastError="";int token=++attempt;
        try {
            var connected=await TcpTransport.Connect(host,port);
            if(token!=attempt||this==null){connected.Dispose();return false;}
            transport=connected;Multiplayer=true;IsConnecting=false;
            var w=new PacketWriter();w.U16(1);w.Text(nickname);Send(Message.Hello,w);
            return true;
        }catch(Exception e){if(token==attempt){IsConnecting=false;LastError=e.Message;}return false;}
    }
    public void CreateRoom(){Send(Message.Create);}
    public void JoinRoom(string code){var w=new PacketWriter();w.Text(code.Trim().ToUpperInvariant());Send(Message.Join,w);}
    public void SetReady(bool ready){var w=new PacketWriter();w.U8((byte)(ready?1:0));Send(Message.Ready,w);}
    public void StartRoom(bool force){var w=new PacketWriter();w.U8((byte)(force?1:0));Send(Message.Start,w);}
    public void ReturnRoom(){Send(Message.Return);}
    public void LeaveRoom(){Send(Message.Leave);}
    public void Loaded()
    {
        if(Prepared==null)return;
        var w=new PacketWriter();w.U32(Prepared.Id);Send(Message.Loaded,w);
    }
    public bool Select(int r0,int c0,int r1,int c1)
    {
        if(SelectionPending||Prepared==null||CurrentRoom==null||CurrentRoom.Phase!=RoomPhase.Playing)return false;
        if(requestId==uint.MaxValue){Fail("Request ID exhausted");return false;}
        var w=new PacketWriter();w.U32(Prepared.Id);w.U32(++requestId);w.U32(revision);
        w.U16((ushort)r0);w.U16((ushort)c0);w.U16((ushort)r1);w.U16((ushort)c1);
        if(!Send(Message.Select,w,false))return false;
        pendingRequest=requestId;SelectionPending=true;selectionTime=Time.realtimeSinceStartup;return true;
    }
    private bool Send(Message type,PacketWriter w=null,bool command=true)
    {
        if(!IsConnected){LastError="Not connected";return false;}
        if(command&&CommandPending)return false;
        if(!transport.Send(type,w==null?Array.Empty<byte>():w.ToArray())){Fail("Send queue full or disconnected");return false;}
        if(command){LastError="";CommandPending=true;commandTime=Time.realtimeSinceStartup;}
        return true;
    }
    private void Update()
    {
        if(transport==null)return;
        try {
            for(int i=0;i<64&&transport!=null&&transport.TryReceive(out var f);i++)Dispatch(f);
        }catch(Exception e){Fail("Invalid server packet: "+e.Message);return;}
        if(transport!=null&&transport.Closed){Fail(transport.Error??"Disconnected");return;}
        if((CommandPending&&Time.realtimeSinceStartup-commandTime>20)||
           (SelectionPending&&Time.realtimeSinceStartup-selectionTime>5))Fail("Server response timed out");
    }
    private void Dispatch(TcpTransport.Frame frame)
    {
        var r=new PacketReader(frame.Body);
        switch(frame.Type)
        {
        case Message.Welcome:
            OwnId=r.U32();r.End();if(OwnId==0)throw new InvalidDataException();CommandPending=false;break;
        case Message.Room:
            var room=new RoomInfo();room.Code=r.Text(4);room.Host=r.U32();room.Phase=(RoomPhase)r.U8();room.Game=r.U32();
            int count=r.U8();if(count<1||count>8||(byte)room.Phase>4)throw new InvalidDataException("Room");
            for(int i=0;i<count;i++)room.Members.Add(new MemberInfo{Id=r.U32(),Name=r.Text(48),Ready=r.Bool(),Finished=r.Bool(),Connected=r.Bool(),Score=r.U32(),Rank=r.U16()});
            r.End();if(room.Find(OwnId)==null)throw new InvalidDataException("Missing local member");
            CurrentRoom=room;CommandPending=false;
            if(room.Phase==RoomPhase.Waiting){Prepared=null;SelectionPending=false;if(SceneManager.GetActiveScene().name==GameConstants.GAME_SCENE)SceneManager.LoadScene(GameConstants.TITLE_SCENE);}
            break;
        case Message.Prepare:
            var game=new GameData{Id=r.U32(),Rows=r.U16(),Columns=r.U16(),Duration=r.U32()};
            if(game.Rows!=GameConstants.ROW||game.Columns!=GameConstants.COLUMN||game.Duration!=120000)throw new InvalidDataException("Unsupported game configuration");
            game.Board=r.Bytes(game.Rows*game.Columns);r.End();
            foreach(byte v in game.Board)if(v<1||v>9)throw new InvalidDataException("Board value");
            if(CurrentRoom==null||CurrentRoom.Game!=game.Id||CurrentRoom.Phase!=RoomPhase.Loading)throw new InvalidDataException("Unexpected prepare");
            Prepared=game;requestId=revision=0;SelectionPending=false;ClockPhase=RoomPhase.Loading;clockRemaining=0;
            SceneManager.LoadScene(GameConstants.GAME_SCENE);break;
        case Message.Clock:
            uint gameId=r.U32();var phase=(RoomPhase)r.U8();uint remaining=r.U32();r.End();
            if(Prepared!=null&&gameId==Prepared.Id){ClockPhase=phase;clockRemaining=remaining;clockReceived=Time.realtimeSinceStartup;CommandPending=false;}
            break;
        case Message.Selection:
            var result=new SelectionResult{Game=r.U32(),Request=r.U32(),Status=r.U8(),Revision=r.U32(),Score=r.U32(),Finished=r.Bool()};
            result.Board=r.Bytes(GameConstants.ROW*GameConstants.COLUMN);r.End();
            if(result.Status>3||result.Score>170)throw new InvalidDataException("Selection");
            foreach(byte v in result.Board)if(v>9)throw new InvalidDataException("Board");
            if(Prepared==null||result.Game!=Prepared.Id||!SelectionPending||result.Request!=pendingRequest)break;
            revision=result.Revision;SelectionPending=false;SelectionReceived?.Invoke(result);break;
        case Message.Left:
            r.End();CurrentRoom=null;Prepared=null;SelectionPending=false;CommandPending=false;
            if(SceneManager.GetActiveScene().name!=GameConstants.TITLE_SCENE)SceneManager.LoadScene(GameConstants.TITLE_SCENE);break;
        case Message.Error:
            LastError=r.Text(256);r.End();CommandPending=false;SelectionPending=false;break;
        default:throw new InvalidDataException("Unknown message");
        }
    }
    private void Fail(string message)
    {
        Disconnect();LastError=message;
        if(SceneManager.GetActiveScene().name!=GameConstants.TITLE_SCENE)SceneManager.LoadScene(GameConstants.TITLE_SCENE);
    }
    public void Disconnect()
    {
        ++attempt;IsConnecting=false;CommandPending=SelectionPending=false;
        var old=transport;transport=null;old?.Dispose();OwnId=0;CurrentRoom=null;Prepared=null;Multiplayer=false;
    }
    private void OnApplicationQuit(){Disconnect();}
    private void OnDestroy(){if(Instance==this){Disconnect();Instance=null;}}
}
