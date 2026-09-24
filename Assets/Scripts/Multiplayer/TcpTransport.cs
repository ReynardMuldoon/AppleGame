using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AppleNet
{
    // No Unity APIs on these workers. Each connection owns separate threads/queues.
    public sealed class TcpTransport : IDisposable
    {
        public struct Frame { public Message Type; public byte[] Body; }
        private readonly TcpClient client; private readonly NetworkStream stream;
        private readonly object gate=new object();
        private readonly Queue<byte[]> sends=new Queue<byte[]>();
        private readonly Queue<Frame> receives=new Queue<Frame>();
        private readonly AutoResetEvent wake=new AutoResetEvent(false);
        private Thread reader,writer; private int closed; private int queuedBytes;
        public bool Closed { get {return Volatile.Read(ref closed)!=0;} }
        public string Error {get;private set;}
        private TcpTransport(TcpClient client)
        {
            this.client=client;client.NoDelay=true;client.SendTimeout=5000;stream=client.GetStream();
            reader=new Thread(ReadLoop){IsBackground=true};writer=new Thread(WriteLoop){IsBackground=true};
            reader.Start();writer.Start();
        }
        public static async Task<TcpTransport> Connect(string host,int port)
        {
            var c=new TcpClient();
            try {
                Task connecting=c.ConnectAsync(host,port);
                if(await Task.WhenAny(connecting,Task.Delay(5000))!=connecting) {
                    c.Close();
                    _=connecting.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("Connection timed out");
                }
                await connecting;return new TcpTransport(c);
            } catch { c.Close();throw; }
        }
        public bool Send(Message type,byte[] body)
        {
            if(body.Length>4092)return false;
            var frame=new byte[body.Length+4];int n=frame.Length;
            frame[0]=(byte)n;frame[1]=(byte)(n>>8);frame[2]=(byte)type;frame[3]=(byte)((ushort)type>>8);
            Buffer.BlockCopy(body,0,frame,4,body.Length);
            lock(gate) {
                if(Closed || sends.Count>=256 || queuedBytes+n>1048576)return false;
                sends.Enqueue(frame);queuedBytes+=n;wake.Set();return true;
            }
        }
        public bool TryReceive(out Frame frame)
        {
            lock(gate) {if(receives.Count==0){frame=default;return false;}frame=receives.Dequeue();return true;}
        }
        private byte[] ReadExact(int n)
        {
            byte[] b=new byte[n];int p=0;
            while(p<n) {int got=stream.Read(b,p,n-p);if(got==0)throw new IOException("Server disconnected");p+=got;}
            return b;
        }
        private void ReadLoop()
        {
            try {
                while(!Closed) {
                    byte[] h=ReadExact(4);int size=h[0]|(h[1]<<8);
                    if(size<4||size>4096)throw new InvalidDataException("Invalid frame size");
                    var f=new Frame{Type=(Message)(h[2]|(h[3]<<8)),Body=ReadExact(size-4)};
                    lock(gate){if(receives.Count>=512)throw new IOException("Receive queue full");receives.Enqueue(f);}
                }
            } catch(Exception e){Close(e.Message);}
        }
        private void WriteLoop()
        {
            try {
                while(!Closed) {
                    byte[] b=null;
                    lock(gate){if(sends.Count>0){b=sends.Dequeue();queuedBytes-=b.Length;}}
                    if(b==null){wake.WaitOne(100);continue;}
                    stream.Write(b,0,b.Length);
                }
            } catch(Exception e){Close(e.Message);}
        }
        private void Close(string error)
        {
            if(Interlocked.Exchange(ref closed,1)!=0)return;
            Error=error;client.Close();wake.Set();
        }
        public void Dispose()
        {
            Close("Disconnected");
            if(reader!=null&&reader!=Thread.CurrentThread)reader.Join(250);
            if(writer!=null&&writer!=Thread.CurrentThread)writer.Join(250);
            // Do not dispose the wait handle while a worker could still access it.
            if((reader==null||!reader.IsAlive)&&(writer==null||!writer.IsAlive))wake.Dispose();
        }
    }
}
