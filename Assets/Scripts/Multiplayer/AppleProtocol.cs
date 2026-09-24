using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AppleNet
{
    public enum Message : ushort
    {
        Hello=1, Create=2, Join=3, Ready=4, Start=5, Loaded=6, Select=7, Leave=8, Return=9,
        Welcome=101, Room=102, Prepare=103, Clock=104, Selection=105, Left=106, Error=107
    }
    public enum RoomPhase : byte { Waiting, Loading, Countdown, Playing, Results }
    public sealed class PacketWriter
    {
        private readonly List<byte> bytes = new List<byte>();
        public void U8(byte v) { bytes.Add(v); }
        public void U16(ushort v) { U8((byte)v); U8((byte)(v>>8)); }
        public void U32(uint v) { for(int i=0;i<4;i++) U8((byte)(v>>(8*i))); }
        public void Text(string s) { byte[] b=Encoding.UTF8.GetBytes(s); if(b.Length>65535)throw new InvalidDataException(); U16((ushort)b.Length);bytes.AddRange(b); }
        public byte[] ToArray() { return bytes.ToArray(); }
    }
    public sealed class PacketReader
    {
        private readonly byte[] bytes; private int pos;
        public PacketReader(byte[] bytes) { this.bytes=bytes; }
        public byte U8() { if(pos>=bytes.Length)throw new InvalidDataException("Short packet");return bytes[pos++]; }
        public bool Bool() { byte v=U8();if(v>1)throw new InvalidDataException("Boolean");return v!=0; }
        public ushort U16() { int a=U8();return (ushort)(a|(U8()<<8)); }
        public uint U32() { uint v=0;for(int i=0;i<4;i++)v|=(uint)U8()<<(8*i);return v; }
        public byte[] Bytes(int n) { if(n<0 || n>bytes.Length-pos)throw new InvalidDataException("Length");byte[] b=new byte[n];Buffer.BlockCopy(bytes,pos,b,0,n);pos+=n;return b; }
        public string Text(int max) { int n=U16();if(n>max)throw new InvalidDataException("String length");return new UTF8Encoding(false,true).GetString(Bytes(n)); }
        public void End() { if(pos!=bytes.Length)throw new InvalidDataException("Trailing bytes"); }
    }
    public sealed class MemberInfo
    {
        public uint Id, Score; public ushort Rank; public string Name;
        public bool Ready, Finished, Connected;
    }
    public sealed class RoomInfo
    {
        public string Code; public uint Host, Game; public RoomPhase Phase;
        public readonly List<MemberInfo> Members=new List<MemberInfo>();
        public MemberInfo Find(uint id) { return Members.Find(p=>p.Id==id); }
    }
    public sealed class GameData
    {
        public uint Id, Duration; public ushort Rows, Columns; public byte[] Board;
    }
    public sealed class SelectionResult
    {
        public uint Game, Request, Revision, Score; public byte Status;
        public bool Finished; public byte[] Board;
    }
}
