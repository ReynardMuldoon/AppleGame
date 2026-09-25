using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AppleNet
{
    // 프로토콜 메시지 ID. 1~9는 클라이언트 요청, 101~107은 서버 응답/통지이다.
    // 서버와 숫자 및 본문 필드 순서를 함께 맞춰야 하며 임의로 재번호를 매기면 안 된다.
    public enum Message : ushort
    {
        Hello = 1, Create = 2, Join = 3, Ready = 4, Start = 5, Loaded = 6, Select = 7, Leave = 8, Return = 9,
        Welcome = 101, Room = 102, Prepare = 103, Clock = 104, Selection = 105, Left = 106, Error = 107
    }
    // 기본값 0부터 Waiting=0, Loading=1, Countdown=2, Playing=3, Results=4.
    // 멤버 순서를 바꾸면 전송 값도 바뀌므로 서버와 함께 수정해야 한다.
    public enum RoomPhase : byte
    {
        Waiting, Loading, Countdown, Playing, Results
    }
    // 패킷 본문 작성기. 정수는 낮은 바이트부터 기록하는 리틀 엔디언 방식이다.
    // 4바이트 전송 헤더는 여기서 만들지 않고 TcpTransport.Send가 붙인다.
    // 인스턴스 하나를 여러 스레드에서 동시에 수정하는 용도로 설계되지 않았다.
    public sealed class PacketWriter
    {
        // 작성 중인 본문. readonly는 참조 재할당만 막으며 목록 내용은 변경 가능하다.
        private readonly List<byte> bytes = new List<byte>();
        // 부호 없는 8비트 값 한 개를 기록한다.
        public void U8(byte v)
        {
            bytes.Add(v);
        }
        // 16비트를 하위 8비트, 상위 8비트 순으로 기록한다.
        public void U16(ushort v)
        {
            U8((byte)v);
            U8((byte)(v >> 8));
        }
        // 32비트를 8비트씩 나누어 네 바이트로 기록한다.
        public void U32(uint v)
        {
            for (int i = 0; i < 4; i++)
                U8((byte)(v >> (8 * i)));
        }
        // 문자열은 [UTF-8 바이트 길이(u16)][UTF-8 데이터] 순서이다. 길이는 문자 수가 아니다.
        // 검토: 65535는 문자열 형식의 한도이며 실제 전송 본문 한도 4092보다 크다.
        // 필드별 제한과 총 패킷 크기는 호출자/전송 계층에서도 검사해야 한다. null은 허용하지 않는다.
        public void Text(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            if (b.Length > 65535)
                throw new InvalidDataException();
            U16((ushort)b.Length);
            bytes.AddRange(b);
        }
        // 작성한 바이트의 복사본을 반환한다. 이후 작성기 변경이 반환 배열에 영향을 주지 않는다.
        public byte[] ToArray()
        {
            return bytes.ToArray();
        }
    }
    // 본문을 순서대로 읽는 상태 있는 리더. 기록 순서와 동일하게 호출해야 한다.
    // 입력 배열을 복사하지 않으므로 읽는 동안 외부에서 배열을 변경하면 안 된다.
    // 검토: 생성자는 null 검사를 하지 않으므로 호출자가 유효한 배열을 보장해야 한다.
    public sealed class PacketReader
    {
        // pos는 다음에 읽을 위치이다. 리더를 여러 스레드에서 공유하지 않는다.
        private readonly byte[] bytes; private int pos;
        public PacketReader(byte[] bytes)
        {
            this.bytes = bytes;
        }
        // 범위를 확인하고 한 바이트를 소비한다. 잘린 본문은 예외로 알린다.
        public byte U8()
        {
            if (pos >= bytes.Length)
                throw new InvalidDataException("Short packet");
            return bytes[pos++];
        }
        // 0=false, 1=true만 허용한다. 다른 값을 true로 묵인하지 않는다.
        public bool Bool()
        {
            byte v = U8();
            if (v > 1)
                throw new InvalidDataException("Boolean");
            return v != 0;
        }
        // 하위 바이트부터 읽어 16비트 값으로 합친다.
        public ushort U16()
        {
            int a = U8();
            return (ushort)(a | (U8() << 8));
        }
        // 각 바이트를 uint로 확장한 뒤 이동하여 32비트 값으로 합친다.
        public uint U32()
        {
            uint v = 0;
            for (int i = 0; i < 4; i++)
                v |= (uint)U8() << (8 * i);
            return v;
        }
        // 남은 본문 길이를 검사하고 n바이트의 복사본을 반환하며 읽기 위치를 전진시킨다.
        public byte[] Bytes(int n)
        {
            if (n < 0 || n > bytes.Length - pos)
                throw new InvalidDataException("Length");
            byte[] b = new byte[n];
            Buffer.BlockCopy(bytes, pos, b, 0, n);
            pos += n;
            return b;
        }
        // max는 허용할 UTF-8 바이트 수이다. 잘못된 UTF-8은 대체 문자로 숨기지 않고 예외로 알린다.
        public string Text(int max)
        {
            int n = U16();
            if (n > max)
                throw new InvalidDataException("String length");
            return new UTF8Encoding(false, true).GetString(Bytes(n));
        }
        // 본문을 정확히 모두 소비했는지 확인한다. 추가 바이트가 있으면 프로토콜 불일치로 처리한다.
        public void End()
        {
            if (pos != bytes.Length)
                throw new InvalidDataException("Trailing bytes");
        }
    }
    // 방 스냅샷에 포함된 참가자 상태. Rank=0은 현재 UI에서 순위 미표시(-)로 사용한다.
    // Finished는 개인 플레이 종료, Connected는 연결 여부이므로 서로 다른 상태이다.
    public sealed class MemberInfo
    {
        public uint Id, Score; public ushort Rank; public string Name;
        public bool Ready, Finished, Connected;
    }
    // 방 코드, 방장 ID, 게임 ID, 단계 및 참가자 스냅샷.
    // Members는 readonly지만 내용은 수정 가능하다. HUD는 목록 순서를 그대로 사용한다.
    public sealed class RoomInfo
    {
        public string Code; public uint Host, Game; public RoomPhase Phase;
        public readonly List<MemberInfo> Members = new List<MemberInfo>();
        // ID가 일치하는 첫 참가자를 선형 탐색하며 없으면 null을 반환한다.
        public MemberInfo Find(uint id)
        {
            return Members.Find(p => p.Id == id);
        }
    }
    // 게임 준비 정보. Duration은 밀리초, Board는 행 우선 초기 숫자 배열이다.
    // 이 DTO 자체는 크기나 값 범위를 검사하지 않으며 수신 처리 계층이 검증한다.
    public sealed class GameData
    {
        public uint Id, Duration; public ushort Rows, Columns; public byte[] Board;
    }
    // 선택 응답: Game은 게임 ID, Request는 요청 번호, Revision은 보드 버전이다.
    // Status는 현재 프로토콜에서 0=성공, 1=합 불일치, 2=버전 불일치, 3=진행 불가이다.
    // Score는 서버 점수, Finished는 개인 종료, Board는 판정 후 개인 보드 전체(0은 빈칸)이다.
    public sealed class SelectionResult
    {
        public uint Game, Request, Revision, Score; public byte Status;
        public bool Finished; public byte[] Board;
    }
}
