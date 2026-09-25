using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AppleNet
{
    // No Unity APIs on these workers. Each connection owns separate threads/queues.
    // 연결 하나마다 수신 스레드와 송신 스레드 및 독립 큐를 소유하는 TCP 전송 계층.
    // 작업 스레드에서는 Unity API를 사용하지 않는다. 수신 결과는 호출자가 TryReceive로 가져간다.
    // TCP의 바이트 흐름을 [총길이 u16][메시지 ID u16][본문] 프레임으로 분리한다.
    public sealed class TcpTransport : IDisposable
    {
        // 헤더를 해석한 수신 단위. Body는 이 프레임에 할당된 배열이며 소비자가 관리한다.
        public struct Frame
        {
            public Message Type; public byte[] Body;
        }
        private readonly TcpClient client; private readonly NetworkStream stream;
        // 송신/수신 큐와 queuedBytes를 보호한다. 잠금을 잡은 채 블로킹 네트워크 I/O를 하지 않는다.
        private readonly object gate = new object();
        private readonly Queue<byte[]> sends = new Queue<byte[]>();
        private readonly Queue<Frame> receives = new Queue<Frame>();
        // 송신 큐에 데이터가 추가되거나 연결을 닫을 때 송신 스레드를 깨운다.
        // 신호는 패킷 개수 카운터가 아니며 실제 작업 유무는 큐에서 확인한다.
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        // queuedBytes는 큐 안에 대기 중인 바이트만 센다. 이미 꺼내 송신 중인 프레임은 제외한다.
        private Thread reader, writer; private int closed; private int queuedBytes;
        // 스레드 간 종료 플래그를 Volatile로 읽는다. 실제 원격 연결 생존을 검사하는 속성은 아니다.
        public bool Closed
        {
            get
            {
                return Volatile.Read(ref closed) != 0;
            }
        }
        // 먼저 Close를 실행한 경로의 종료 사유.
        // 검토: closed를 먼저 게시하고 Error를 나중에 쓰므로 다른 스레드가 Closed=true와
        // 아직 설정되지 않은 Error를 함께 볼 수 있다. 종료 사유 공개 순서의 동기화 개선이 필요하다.
        public string Error
        {
            get; private set;
        }
        // 연결된 소켓에만 생성한다. NoDelay는 Nagle 지연을 끄며 전달 시간 자체를 보장하지 않는다.
        // SendTimeout은 동기 쓰기 제한이며 게임 응답 대기 시간과는 별개이다.
        private TcpTransport(TcpClient client)
        {
            this.client = client;
            client.NoDelay = true;
            client.SendTimeout = 5000;
            stream = client.GetStream();
            reader = new Thread(ReadLoop) { IsBackground = true };
            writer = new Thread(WriteLoop) { IsBackground = true };
            reader.Start();
            writer.Start();
        }
        // TCP 연결과 5초 지연 중 먼저 끝난 작업을 기다린다. 게임 Hello 승인은 여기서 다루지 않는다.
        public static async Task<TcpTransport> Connect(string host, int port)
        {
            var c = new TcpClient();
            try
            {
                Task connecting = c.ConnectAsync(host, port);
                if (await Task.WhenAny(connecting, Task.Delay(5000)) != connecting)
                {
                    c.Close();
                    // 시간 초과 후 연결 작업이 늦게 실패하더라도 예외를 관찰한다.
                    // WhenAny 자체는 연결 작업을 취소하지 않으므로 위에서 소켓을 닫는다.
                    _ = connecting.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("Connection timed out");
                }
                // 먼저 완료되어도 연결 실패일 수 있으므로 await로 성공 여부/예외를 확인한다.
                await connecting;
                return new TcpTransport(c);
            }
            catch { c.Close(); throw; }
        }
        // 본문을 복사하여 프레임을 만들고 큐 등록 성공 여부를 반환한다. 서버 수신/처리 보장은 아니다.
        // 본문은 null이 아니어야 하며 복사 중 다른 스레드에서 수정하지 않아야 한다.
        public bool Send(Message type, byte[] body)
        {
            // 최대 총길이 4096에서 헤더 4바이트를 제외한 본문 한도이다.
            if (body.Length > 4092)
                return false;
            var frame = new byte[body.Length + 4];
            int n = frame.Length;
            // 총길이는 헤더를 포함한다. 길이와 메시지 ID 모두 리틀 엔디언으로 기록한다.
            frame[0] = (byte)n;
            frame[1] = (byte)(n >> 8);
            frame[2] = (byte)type;
            frame[3] = (byte)((ushort)type >> 8);
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            lock (gate)
            {
                // 프레임 개수 256개와 대기 바이트 1MiB를 제한하여 무제한 메모리 증가를 막는다.
                // 검토: Close는 gate 없이 플래그를 바꾸므로 검사 직후 종료될 수도 있다.
                // 따라서 true가 반환되어도 이후 연결 종료로 해당 프레임이 전송되지 않을 수 있다.
                if (Closed || sends.Count >= 256 || queuedBytes + n > 1048576)
                    return false;
                sends.Enqueue(frame);
                queuedBytes += n;
                wake.Set();
                return true;
            }
        }
        // 큐가 비어 있으면 false를 반환하며 기다리지 않는다. 닫힌 연결에도 남은 프레임은 꺼낼 수 있다.
        public bool TryReceive(out Frame frame)
        {
            lock (gate)
            {
                if (receives.Count == 0)
                {
                    frame = default;
                    return false;
                }
                frame = receives.Dequeue();
                return true;
            }
        }
        // TCP Read 한 번이 요청한 길이를 모두 채운다는 보장은 없다.
        // 여러 번 읽어 정확히 n바이트를 모으고, 0바이트 반환은 상대 종료로 처리한다.
        // 읽기 타임아웃은 별도로 설정하지 않았으므로 무응답 시 읽기는 연결이 닫힐 때까지 대기할 수 있다.
        private byte[] ReadExact(int n)
        {
            byte[] b = new byte[n];
            int p = 0;
            while (p < n)
            {
                int got = stream.Read(b, p, n - p);
                if (got == 0)
                    throw new IOException("Server disconnected");
                p += got;
            }
            return b;
        }
        // 헤더를 먼저 모아 크기를 검증한 뒤 본문을 읽는다. 메시지별 본문 의미는 상위 계층이 검증한다.
        private void ReadLoop()
        {
            try
            {
                while (!Closed)
                {
                    byte[] h = ReadExact(4);
                    int size = h[0] | (h[1] << 8);
                    if (size < 4 || size > 4096)
                        throw new InvalidDataException("Invalid frame size");
                    var f = new Frame { Type = (Message)(h[2] | (h[3] << 8)), Body = ReadExact(size - 4) };
                    lock (gate)
                    {
                        // 수신자가 처리하지 못하는 상태에서 큐가 계속 자라지 않도록 연결을 닫는다.
                        // 이는 점수와 무관하다. Unity Update가 정지해도 수신 스레드는 계속 누적할 수 있다.
                        if (receives.Count >= 512)
                            throw new IOException("Receive queue full");
                        receives.Enqueue(f);
                    }
                }
            }
            catch (Exception e) { Close(e.Message); }
        }
        // 한 송신 스레드가 큐 순서대로 프레임 전체를 동기 Write한다.
        // NetworkStream.Write를 사용하므로 저수준 Socket.Send처럼 반환 길이를 누적하는 루프는 없다.
        private void WriteLoop()
        {
            try
            {
                while (!Closed)
                {
                    byte[] b = null;
                    lock (gate)
                    {
                        if (sends.Count > 0)
                        {
                            b = sends.Dequeue();
                            queuedBytes -= b.Length;
                        }
                    }
                    // 큐가 비면 신호 또는 100ms 경과를 기다린 뒤 다시 확인한다. 바쁜 반복을 피한다.
                    if (b == null)
                    {
                        wake.WaitOne(100);
                        continue;
                    }
                    // 잠금 밖에서 쓰므로 상대가 느리더라도 큐 접근 잠금을 점유하지 않는다.
                    stream.Write(b, 0, b.Length);
                }
            }
            catch (Exception e) { Close(e.Message); }
        }
        // Interlocked로 여러 종료 경로 중 첫 호출만 소켓 종료와 사유 기록을 수행한다.
        // 대기 중 송신을 모두 보내는 graceful flush가 아니며 큐의 패킷은 유실될 수 있다.
        private void Close(string error)
        {
            if (Interlocked.Exchange(ref closed, 1) != 0)
                return;
            Error = error;
            client.Close();
            wake.Set();
        }
        // 소켓을 닫아 I/O 종료를 유도하고 각 스레드 종료를 최대 250ms씩 기다린다.
        // 현재 스레드를 Join하면 자기 자신을 기다리게 되므로 제외한다.
        // 검토: Join 반환값을 검사하지 않아 반환 시 모든 작업 스레드 종료를 보장하지 않는다.
        // 늦게 종료된 스레드에 대한 대기 핸들 정리가 이 메서드 이후 자동 재시도되지는 않는다.
        // 명시적인 종료 완료/단일 자원 해제 절차를 고려해야 한다.
        public void Dispose()
        {
            Close("Disconnected");
            if (reader != null && reader != Thread.CurrentThread)
                reader.Join(250);
            if (writer != null && writer != Thread.CurrentThread)
                writer.Join(250);
            // 살아 있는 작업 스레드가 WaitOne/Set을 호출할 수 있으므로 핸들을 먼저 파괴하지 않는다.
            // 검토: 이 조건은 외부 Send 호출과의 경합까지 막지는 않는다. Dispose와 Send를
            // 동시에 호출하지 않도록 사용 규약을 정하거나 종료와 핸들 사용을 함께 동기화해야 한다.
            // Do not dispose the wait handle while a worker could still access it.
            if ((reader == null || !reader.IsAlive) && (writer == null || !writer.IsAlive))
                wake.Dispose();
        }
    }
}
