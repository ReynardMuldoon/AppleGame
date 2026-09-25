using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AppleNet;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 클라이언트의 서버 연결과 멀티플레이 상태를 관리한다.
/// 바이트 송수신은 TcpTransport에 맡기고, 수신 큐의 패킷은 Update에서 해석한다.
/// 씬 전환 및 이벤트 구독자 호출이 포함되므로 공개 API는 Unity 메인 스레드에서 사용한다.
/// 게임 판정은 서버가 담당하며 이 클래스는 요청 전송과 서버 상태 반영을 담당한다.
/// </summary>
public class NetworkManager : MonoBehaviour
{
    // 씬이 변경되어도 유지되는 단일 매니저. 중복 객체는 Awake에서 제거한다.
    public static NetworkManager Instance
    {
        get; private set;
    }
    /// <summary>
    /// 매니저가 없으면 생성하고 반환한다. 활성 객체에 컴포넌트를 추가하면 Awake가 실행된다.
    /// Unity 객체를 생성하므로 메인 스레드에서 호출한다.
    /// </summary>
    public static NetworkManager Ensure()
    {
        if (Instance == null)
            new GameObject("NetworkManager").AddComponent<NetworkManager>();
        return Instance;
    }
    // 멀티플레이 모드 여부. 방 입장 여부나 게임 진행 여부와는 별개이다.
    public bool Multiplayer
    {
        get; private set;
    }
    // 로컬 전송 객체가 아직 닫히지 않았는지 확인한다.
    // 서버의 Hello 승인이나 원격 연결의 실시간 생존까지 보장하는 값은 아니다.
    public bool IsConnected
    {
        get
        {
            return transport != null && !transport.Closed;
        }
    }
    // 비동기 TCP 연결을 시도하는 동안 중복 연결 요청을 막는다.
    public bool IsConnecting
    {
        get; private set;
    }
    // Welcome으로 유효한 플레이어 ID를 받은 상태. 계정 인증을 뜻하지는 않는다.
    public bool Handshaken
    {
        get
        {
            return IsConnected && OwnId != 0;
        }
    }
    // 서버가 부여한 내 플레이어 ID. 0은 아직 ID를 받지 않았거나 연결이 정리된 상태이다.
    public uint OwnId
    {
        get; private set;
    }
    // 마지막 Room 패킷으로 받은 방 전체 상태. null이면 현재 보유한 방 정보가 없다.
    public RoomInfo CurrentRoom
    {
        get; private set;
    }
    // Prepare로 받은 게임 ID와 초기 보드. 플레이 도중의 최신 개인 보드와는 구분한다.
    public GameData Prepared
    {
        get; private set;
    }
    // 마지막 시간 통지의 단계. 방 전체 단계는 CurrentRoom.Phase로 별도 관리한다.
    public RoomPhase ClockPhase
    {
        get; private set;
    }
    // UI에 표시할 최근 오류. 모든 오류가 연결 종료를 의미하는 것은 아니다.
    public string LastError { get; private set; } = "";
    // Hello 및 방 관련 명령의 응답 대기 표시. 요청별 ID를 대조하는 구조는 아니며,
    // Welcome, Room, 유효한 Clock 등의 수신에서도 해제된다.
    public bool CommandPending
    {
        get; private set;
    }
    // 선택 요청은 한 번에 하나만 전송한다. 해당 응답을 받기 전에는 다음 선택을 막는다.
    public bool SelectionPending
    {
        get; private set;
    }
    /// <summary>
    /// 현재 대기 중인 선택 요청과 일치하는 서버 결과를 메인 스레드에서 전달한다.
    /// 실패 판정도 전달되므로 구독자는 결과 상태와 서버 보드를 함께 사용해야 한다.
    /// 씬 객체인 구독자는 파괴될 때 구독을 해제해야 한다.
    /// </summary>
    public event Action<SelectionResult> SelectionReceived;
    // 실제 송수신 및 큐 관리는 전송 객체에 위임한다. 이 클래스에는 별도 잠금이 없다.
    private TcpTransport transport;
    // 연결 시도의 세대 번호. Disconnect 이후 늦게 완료된 예전 연결을 채택하지 않도록 한다.
    private int attempt;

    // requestId: 현재 게임의 선택 요청 순번.
    // revision: 마지막으로 채택한 선택 응답의 보드 버전.
    // pendingRequest: 현재 응답을 기다리는 요청 번호.
    private uint requestId, revision, pendingRequest;
    // 각 요청의 시작 시각과 마지막 시간 패킷을 처리한 시각(초).
    private float commandTime, selectionTime, clockReceived;
    // 마지막 서버 시간 통지의 남은 시간(밀리초).
    private uint clockRemaining;
    /// <summary>
    /// 마지막 시간 통지에서 로컬 경과 시간을 빼 화면 표시용 남은 초를 추정한다.
    /// 네트워크 지연을 보정하는 정밀 동기화가 아니며, 게임 종료 판정은 서버가 담당한다.
    /// timeScale의 영향을 받지 않는 realtimeSinceStartup을 사용한다.
    /// </summary>
    public float RemainingSeconds
    {
        get
        {
            return Mathf.Max(0, clockRemaining / 1000f - (Time.realtimeSinceStartup - clockReceived));
        }
    }
    // 씬 전환 중 중복 생성은 막고, 기존 연결과 방 상태를 유지한다.
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // 창이 포커스를 잃어도 Update에서 수신 큐를 처리하도록 한다.
        // 에디터 Pause나 디버거 중단점에 의한 정지까지 해제하는 설정은 아니다.
        Application.runInBackground = true;
        if (GetComponent<MultiplayerHud>() == null)
            gameObject.AddComponent<MultiplayerHud>();
    }
    /// <summary>서버 연결을 정리하고 오프라인 싱글플레이 씬으로 이동한다.</summary>
    public void StartSingle()
    {
        Disconnect();
        Multiplayer = false;
        LastError = "";
        SceneManager.LoadScene(GameConstants.GAME_SCENE);
    }
    /// <summary>
    /// TCP 연결 후 프로토콜 버전과 닉네임을 Hello로 전송한다.
    /// 메인 스레드에서 호출하여 await 이후 Unity 상태 변경도 같은 실행 문맥에서 수행한다.
    /// </summary>
    /// <returns>
    /// 연결을 채택하고 Hello 전송을 시도하면 true. 서버의 승인 완료를 뜻하지 않는다.
    /// 현재 구현은 Hello의 Send 반환값을 검사하지 않으므로 전송 성공 자체도 보장하지 않는다.
    /// 방 관련 UI는 별도로 Handshaken을 확인해야 한다.
    /// </returns>
    public async Task<bool> ConnectAsync(string host, int port, string nickname)
    {
        if (IsConnecting || IsConnected)
            return false;
        // 닉네임 제한은 문자 수가 아니라 패킷에 들어갈 UTF-8 바이트 수 기준이다.
        nickname = nickname.Trim();
        if (nickname.Length == 0 || Encoding.UTF8.GetByteCount(nickname) > 48)
        {
            LastError = "Nickname must be 1-48 UTF-8 bytes.";
            return false;
        }
        IsConnecting = true;
        LastError = "";
        int token = ++attempt;
        try
        {
            var connected = await TcpTransport.Connect(host, port);
            // 기다리는 동안 연결 취소/새 시도/객체 파괴가 있었으면 이 연결은 폐기한다.
            // attempt는 기존 비동기 작업을 취소하는 대신 그 결과의 채택을 막는 장치이다.
            if (token != attempt || this == null)
            {
                connected.Dispose();
                return false;
            }
            transport = connected;
            Multiplayer = true;
            IsConnecting = false;
            var w = new PacketWriter();
            // Hello 본문: 프로토콜 버전(u16), 닉네임(문자열). 서버와 기록 순서가 같아야 한다.
            w.U16(1);
            w.Text(nickname);
            Send(Message.Hello, w);
            return true;
        }
        catch (Exception e)
        {
            // 과거 연결 시도의 실패가 새 연결 상태를 덮어쓰지 않게 한다.
            if (token == attempt)
            {
                IsConnecting = false;
                LastError = e.Message;
            }
            return false;
        }
    }
    /// <summary>방 생성을 요청한다. 실제 방 정보는 서버의 Room 응답으로 반영한다.</summary>
    public void CreateRoom()
    {
        Send(Message.Create);
    }
    /// <summary>입력 코드의 앞뒤 공백을 제거하고 대문자로 통일하여 방 참가를 요청한다.</summary>
    public void JoinRoom(string code)
    {
        var w = new PacketWriter();
        w.Text(code.Trim().ToUpperInvariant());
        Send(Message.Join, w);
    }
    /// <summary>준비 또는 준비 취소를 요청한다. bool은 패킷에서 0/1 한 바이트로 표현한다.</summary>
    public void SetReady(bool ready)
    {
        var w = new PacketWriter();
        w.U8((byte)(ready ? 1 : 0));
        Send(Message.Ready, w);
    }
    /// <summary>
    /// 방 시작을 요청한다. force는 다른 참가자의 준비 상태를 무시할지 지정한다.
    /// 방장 권한, 최소 인원, 게임 상태 등 시작 허용 조건은 서버가 최종 검증한다.
    /// </summary>
    public void StartRoom(bool force)
    {
        var w = new PacketWriter();
        w.U8((byte)(force ? 1 : 0));
        Send(Message.Start, w);
    }
    /// <summary>결과 화면에서 방 전체의 대기실 복귀를 요청한다. 방장 권한은 서버가 검사한다.</summary>
    public void ReturnRoom()
    {
        Send(Message.Return);
    }
    /// <summary>방 퇴장을 요청한다. 서버의 Left 응답을 받은 뒤 방 상태와 화면을 정리한다.</summary>
    public void LeaveRoom()
    {
        Send(Message.Leave);
    }
    /// <summary>
    /// 게임 씬과 보드 준비를 마친 호출자가 현재 게임의 로딩 완료를 알린다.
    /// 이 함수 자체가 씬이나 보드의 로딩 완료 여부를 검사하지는 않는다.
    /// </summary>
    public void Loaded()
    {
        if (Prepared == null)
            return;
        var w = new PacketWriter();
        w.U32(Prepared.Id);
        Send(Message.Loaded, w);
    }
    /// <summary>
    /// 사각형 선택을 서버에 제출한다. 범위는 행 [r0, r1), 열 [c0, c1)이다.
    /// 요청자는 보드 범위의 좌표를 전달해야 하며, 영역과 합의 유효성은 서버가 판정한다.
    /// </summary>
    /// <returns>송신 큐 등록 성공 여부. 사과 제거 성공 여부는 Selection 응답으로 확인한다.</returns>
    public bool Select(int r0, int c0, int r1, int c1)
    {
        if (SelectionPending || Prepared == null || CurrentRoom == null || CurrentRoom.Phase != RoomPhase.Playing)
            return false;
        // 요청 번호가 순환하면 과거 요청과 혼동할 수 있으므로 새 요청을 만들지 않는다.
        if (requestId == uint.MaxValue)
        {
            Fail("Request ID exhausted");
            return false;
        }
        var w = new PacketWriter();
        w.U32(Prepared.Id);
        // 요청 번호는 응답 대응용, revision은 서버가 오래된 보드 기준 요청인지 검사하는 용도이다.
        w.U32(++requestId);
        w.U32(revision);
        w.U16((ushort)r0);
        w.U16((ushort)c0);
        w.U16((ushort)r1);
        w.U16((ushort)c1);
        // 선택은 일반 명령 대기 플래그 대신 SelectionPending으로 독립 관리한다.
        if (!Send(Message.Select, w, false))
            return false;
        pendingRequest = requestId;
        SelectionPending = true;
        selectionTime = Time.realtimeSinceStartup;
        return true;
    }
    /// <summary>
    /// 본문을 전송 계층의 송신 큐에 등록한다. w가 없으면 빈 본문을 사용한다.
    /// command=true인 일반 명령은 동시에 하나만 대기하도록 제한한다.
    /// 반환값 true는 서버 수신이나 처리 완료를 보장하지 않는다.
    /// </summary>
    private bool Send(Message type, PacketWriter w = null, bool command = true)
    {
        if (!IsConnected)
        {
            LastError = "Not connected";
            return false;
        }
        if (command && CommandPending)
            return false;
        if (!transport.Send(type, w == null ? Array.Empty<byte>() : w.ToArray()))
        {
            Fail("Send queue full or disconnected");
            return false;
        }
        if (command)
        {
            LastError = "";
            CommandPending = true;
            commandTime = Time.realtimeSinceStartup;
        }
        return true;
    }
    // Unity 메인 스레드에서 패킷 반영, 연결 종료 감지, 응답 대기 시간 초과를 처리한다.
    private void Update()
    {
        if (transport == null)
            return;
        try
        {
            // 한 프레임에 최대 64개만 처리하여 패킷 처리가 프레임을 과도하게 점유하지 않게 한다.
            // Dispatch 중 상태가 바뀔 수 있으므로 매 반복마다 transport를 다시 확인한다.
            // 백그라운드에서 이 루프가 멈추면 수신 스레드의 큐에 메시지가 누적될 수 있다.
            for (int i = 0; i < 64 && transport != null && transport.TryReceive(out var f); i++)
                Dispatch(f);
        }
        catch (Exception e)
        {
            // 이 범위에는 파싱뿐 아니라 Dispatch와 이벤트 구독자의 예외도 포함된다.
            Fail("Invalid server packet: " + e.Message);
            return;
        }
        if (transport != null && transport.Closed)
        {
            Fail(transport.Error ?? "Disconnected");
            return;
        }
        // 실제 송신 완료 시각이 아니라 큐 등록 시점부터 계산한다.
        // 먼저 수신 큐를 처리한 뒤 남아 있는 일반 명령(20초)/선택 요청(5초)의 대기를 검사한다.
        if ((CommandPending && Time.realtimeSinceStartup - commandTime > 20) ||
           (SelectionPending && Time.realtimeSinceStartup - selectionTime > 5))
            Fail("Server response timed out");
    }
    /// <summary>
    /// 패킷 종류에 맞는 순서로 본문을 읽고 로컬 상태에 반영한다.
    /// Read 도중의 오류나 잘못된 값은 예외로 전달되어 Update에서 연결 종료로 처리된다.
    /// 각 r.End()는 본문을 정확히 소비했는지 검사하기 위한 호출이다.
    /// </summary>
    private void Dispatch(TcpTransport.Frame frame)
    {
        var r = new PacketReader(frame.Body);
        switch (frame.Type)
        {
            // Hello 승인: 내 ID를 저장하여 핸드셰이크 완료 상태로 전환한다.
            case Message.Welcome:
                OwnId = r.U32();
                r.End();
                if (OwnId == 0)
                    throw new InvalidDataException();
                CommandPending = false;
                break;
            // 방 전체 스냅샷: 기존 목록에 덧붙이지 않고 수신한 방 상태로 교체한다.
            case Message.Room:
                var room = new RoomInfo();
                room.Code = r.Text(4);
                room.Host = r.U32();
                room.Phase = (RoomPhase)r.U8();
                room.Game = r.U32();
                int count = r.U8();
                if (count < 1 || count > 8 || (byte)room.Phase > 4)
                    throw new InvalidDataException("Room");
                for (int i = 0; i < count; i++)
                    room.Members.Add(new MemberInfo { Id = r.U32(), Name = r.Text(48), Ready = r.Bool(), Finished = r.Bool(), Connected = r.Bool(), Score = r.U32(), Rank = r.U16() });
                r.End();
                if (room.Find(OwnId) == null)
                    throw new InvalidDataException("Missing local member");
                CurrentRoom = room;
                CommandPending = false;
                // 대기실 복귀 시 게임 준비 정보를 해제한다. 타이틀 씬에서 방 UI를 표시하므로
                // 이 씬 이동은 연결 종료나 방 퇴장을 의미하지 않는다.
                if (room.Phase == RoomPhase.Waiting)
                {
                    Prepared = null;
                    SelectionPending = false;
                    if (SceneManager.GetActiveScene().name == GameConstants.GAME_SCENE)
                        SceneManager.LoadScene(GameConstants.TITLE_SCENE);
                }
                break;
            // 새 게임의 공통 초기 보드. 현재 지원하는 크기와 120초 설정만 허용한다.
            case Message.Prepare:
                var game = new GameData { Id = r.U32(), Rows = r.U16(), Columns = r.U16(), Duration = r.U32() };
                if (game.Rows != GameConstants.ROW || game.Columns != GameConstants.COLUMN || game.Duration != 120000)
                    throw new InvalidDataException("Unsupported game configuration");
                game.Board = r.Bytes(game.Rows * game.Columns);
                r.End();
                foreach (byte v in game.Board)
                    if (v < 1 || v > 9)
                        throw new InvalidDataException("Board value");
                if (CurrentRoom == null || CurrentRoom.Game != game.Id || CurrentRoom.Phase != RoomPhase.Loading)
                    throw new InvalidDataException("Unexpected prepare");
                Prepared = game;
                // 새 게임에서는 이전 게임의 요청 순번과 보드 버전을 재사용하지 않는다.
                requestId = revision = 0;
                SelectionPending = false;
                ClockPhase = RoomPhase.Loading;
                clockRemaining = 0;
                SceneManager.LoadScene(GameConstants.GAME_SCENE);
                break;
            // 현재 게임의 시간 정보만 채택한다. 처리 시각을 기록하여 화면에서 경과 시간을 뺀다.
            case Message.Clock:
                uint gameId = r.U32();
                var phase = (RoomPhase)r.U8();
                uint remaining = r.U32();
                r.End();
                if (Prepared != null && gameId == Prepared.Id)
                {
                    ClockPhase = phase;
                    clockRemaining = remaining;
                    clockReceived = Time.realtimeSinceStartup;
                    CommandPending = false;
                }
                break;
            // 서버가 판정한 내 선택 결과와 개인 보드 전체를 읽는다. 보드의 0은 제거된 빈칸이다.
            case Message.Selection:
                var result = new SelectionResult { Game = r.U32(), Request = r.U32(), Status = r.U8(), Revision = r.U32(), Score = r.U32(), Finished = r.Bool() };
                result.Board = r.Bytes(GameConstants.ROW * GameConstants.COLUMN);
                r.End();
                if (result.Status > 3 || result.Score > 170)
                    throw new InvalidDataException("Selection");
                foreach (byte v in result.Board)
                    if (v > 9)
                        throw new InvalidDataException("Board");
                // 다른 게임, 이미 처리한 응답, 현재 대기 요청과 다른 응답은 반영하지 않는다.
                if (Prepared == null || result.Game != Prepared.Id || !SelectionPending || result.Request != pendingRequest)
                    break;
                revision = result.Revision;
                SelectionPending = false;
                // 서버 보드를 적용하는 일은 구독자에게 맡긴다. 현재 마우스 선택을 재사용하지 않는다.
                // 이벤트는 동기 호출이므로 구독자 예외도 Update의 catch로 전달된다.
                SelectionReceived?.Invoke(result);
                break;
            // 퇴장 승인: 방/게임 정보만 정리한다. TCP 연결과 OwnId는 유지한다.
            case Message.Left:
                r.End();
                CurrentRoom = null;
                Prepared = null;
                SelectionPending = false;
                CommandPending = false;
                if (SceneManager.GetActiveScene().name != GameConstants.TITLE_SCENE)
                    SceneManager.LoadScene(GameConstants.TITLE_SCENE);
                break;
            // 서버의 요청 거절 안내는 연결을 유지한 채 표시한다.
            // 현재 구현은 어떤 요청의 오류인지 구분하지 않고 두 대기 플래그를 모두 해제한다.
            case Message.Error:
                LastError = r.Text(256);
                r.End();
                CommandPending = false;
                SelectionPending = false;
                break;
            default:
                throw new InvalidDataException("Unknown message");
        }
    }
    // 통신/패킷 처리 실패: 연결을 정리하고 오류 문구를 남겨 타이틀에서 표시한다.
    private void Fail(string message)
    {
        Disconnect();
        LastError = message;
        if (SceneManager.GetActiveScene().name != GameConstants.TITLE_SCENE)
            SceneManager.LoadScene(GameConstants.TITLE_SCENE);
    }
    /// <summary>
    /// 진행 중인 연결 시도의 결과를 무효화하고 전송 객체 및 방 상태를 정리한다.
    /// 이 함수 자체는 씬을 이동하거나 LastError를 지우지 않는다.
    /// </summary>
    public void Disconnect()
    {
        ++attempt;
        IsConnecting = false;
        CommandPending = SelectionPending = false;
        var old = transport;
        // 필드를 먼저 비워 더 이상 기존 연결을 사용하지 않도록 한 뒤 리소스 정리를 위임한다.
        transport = null;
        old?.Dispose();
        OwnId = 0;
        CurrentRoom = null;
        Prepared = null;
        Multiplayer = false;
    }
    // 종료 시 남아 있는 연결 리소스를 정리한다.
    private void OnApplicationQuit()
    {
        Disconnect();
    }
    // 중복 생성되어 제거되는 객체가 정상 싱글턴의 연결/참조를 지우지 않도록 소유자를 확인한다.
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Disconnect();
            Instance = null;
        }
    }
}
