using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// C++ 서버의 enum class와 완벽히 일치해야 하는 패킷 타입
public enum PacketType : ushort
{
    REQ_GAME_START = 1,
    RES_GAME_START = 2,
    REQ_DRAG_APPLE = 3,
    RES_DRAG_APPLE = 4
}

public class NetworkManager : MonoBehaviour
{
    // 어느 씬에서든 접근할 수 있도록 싱글톤 패턴 적용
    public static NetworkManager Instance { get; private set; }

    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread receiveThread;

    // 상태 기계(State Machine)를 위한 플래그 변수들
    public bool IsConnected { get; private set; } = false;
    public bool IsConnecting { get; private set; } = false;

    // 백그라운드 수신 스레드와 메인 스레드 사이의 안전한 중재자 큐
    private ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    // 외부(GameManager 등)에서 구독할 수 있는 이벤트 방송국
    public event Action<byte[]> OnGameStartReceived;
    public event Action<bool, int> OnDragResultReceived;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬이 변경되어도 파괴되지 않음
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        // 유니티 메인 스레드에서만 안전하게 UI나 이벤트를 터뜨리기 위해 큐를 소비합니다.
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }
    }

    /// <summary>
    /// 서버에 비동기로 연결을 시도합니다. UI 프레임 드랍이 발생하지 않습니다.
    /// TitleScene에서 버튼을 눌렀을 때 호출하면 됩니다.
    /// </summary>
    public async Task<bool> ConnectAsync(string ip, int port)
    {
        if (IsConnected || IsConnecting) return false;

        IsConnecting = true;
        Debug.Log($"[네트워크] 서버({ip}:{port}) 연결 시도 중...");

        try
        {
            tcpClient = new TcpClient();
            // 비동기 연결 시도 (유니티 화면이 멈추지 않음)
            await tcpClient.ConnectAsync(ip, port);

            stream = tcpClient.GetStream();
            IsConnected = true;
            IsConnecting = false;

            Debug.Log("[네트워크] 서버 접속 성공!");

            // 연결 성공 시, 데이터를 백그라운드에서 퍼담을 일꾼 스레드 가동
            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[네트워크] 접속 실패: {e.Message}");
            IsConnecting = false;
            return false;
        }
    }


    // [C -> S] 게임 시작(보드 생성) 요청
    public void SendGameStartRequest()
    {
        if (!IsConnected || stream == null) return;

        ushort size = 4; // Header: Size(2) + Type(2)
        ushort type = (ushort)PacketType.REQ_GAME_START;

        byte[] packet = new byte[size];
        Buffer.BlockCopy(BitConverter.GetBytes(size), 0, packet, 0, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(type), 0, packet, 2, 2);

        stream.Write(packet, 0, packet.Length);
        Debug.Log("[네트워크] REQ_GAME_START 전송 완료");
    }

    // [C -> S] 드래그한 사과 정보 서버에 검증 요청
    public void SendDragAppleRequest(List<int> appleIndices)
    {
        if (!IsConnected || stream == null || appleIndices == null || appleIndices.Count == 0) return;

        // Header(4) + indexCount(4) + (인덱스 배열 크기 * 4)
        ushort size = (ushort)(4 + 4 + (appleIndices.Count * 4));
        ushort type = (ushort)PacketType.REQ_DRAG_APPLE;

        byte[] packet = new byte[size];

        // 1. 헤더 복사
        Buffer.BlockCopy(BitConverter.GetBytes(size), 0, packet, 0, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(type), 0, packet, 2, 2);

        // 2. 사과 개수 (indexCount) 복사
        Buffer.BlockCopy(BitConverter.GetBytes(appleIndices.Count), 0, packet, 4, 4);

        // 3. 인덱스 배열 데이터 복사
        int offset = 8;
        for (int i = 0; i < appleIndices.Count; i++)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(appleIndices[i]), 0, packet, offset, 4);
            offset += 4;
        }

        stream.Write(packet, 0, packet.Length);
    }


    // 🚨 절대 이 함수 안에서 유니티 GameObject나 UI를 직접 건드리면 안 됩니다. (엔진 에러 발생)
    private void ReceiveLoop()
    {
        byte[] headerBuffer = new byte[4];

        while (IsConnected)
        {
            try
            {
                // 1. 헤더 (4바이트) 수신 - TCP 단편화 방어
                int headerRead = 0;
                while (headerRead < 4)
                {
                    int read = stream.Read(headerBuffer, headerRead, 4 - headerRead);
                    if (read == 0) throw new Exception("서버에서 연결을 종료했습니다.");
                    headerRead += read;
                }

                ushort size = BitConverter.ToUInt16(headerBuffer, 0);
                ushort type = BitConverter.ToUInt16(headerBuffer, 2);

                // 2. 페이로드 (본문) 수신
                int payloadSize = size - 4;
                byte[] payloadBuffer = new byte[payloadSize];

                int payloadRead = 0;
                while (payloadRead < payloadSize)
                {
                    int read = stream.Read(payloadBuffer, payloadRead, payloadSize - payloadRead);
                    if (read == 0) throw new Exception("서버에서 연결을 종료했습니다.");
                    payloadRead += read;
                }

                // 3. 패킷 타입에 따른 로직 분배 (Dispatch)
                if (type == (ushort)PacketType.RES_GAME_START)
                {
                    // 170바이트 사과 배열 복원
                    byte[] appleGrid = new byte[170];
                    Buffer.BlockCopy(payloadBuffer, 0, appleGrid, 0, 170);

                    // 메인 스레드 큐에 이벤트 발생 작업 예약
                    mainThreadActions.Enqueue(() =>
                    {
                        Debug.Log("[네트워크] 170개 사과 데이터 수신 완료.");
                        OnGameStartReceived?.Invoke(appleGrid);
                    });
                }
                else if (type == (ushort)PacketType.RES_DRAG_APPLE)
                {
                    // C++: #pragma pack(1) 기준 bool(1바이트) + int(4바이트) = 총 5바이트
                    bool isSuccess = BitConverter.ToBoolean(payloadBuffer, 0);
                    int currentScore = BitConverter.ToInt32(payloadBuffer, 1);

                    mainThreadActions.Enqueue(() =>
                    {
                        OnDragResultReceived?.Invoke(isSuccess, currentScore);
                    });
                }
            }
            catch (Exception e)
            {
                if (IsConnected) Debug.LogWarning($"[네트워크] 수신 에러 또는 접속 종료: {e.Message}");
                Disconnect();
                break;
            }
        }
    }

    public void Disconnect()
    {
        if (!IsConnected) return;

        IsConnected = false;
        IsConnecting = false;
        stream?.Close();
        tcpClient?.Close();

        Debug.Log("[네트워크] 소켓 연결이 해제되었습니다.");
    }

    private void OnDestroy()
    {
        Disconnect();

        // 스레드가 정상 종료되도록 약간 대기
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(500);
        }
    }
}