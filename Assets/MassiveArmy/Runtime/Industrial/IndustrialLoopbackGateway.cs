using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace MassiveArmy.Industrial {
    public sealed class IndustrialWireRequest {public string ticket;public IndustrialCommand command;}
    public sealed class IndustrialWireReply {public bool accepted;public string message;public long entity;}
    /// <summary>Loopback-only transport probe. Production discovery, authentication and replication are separate gates.</summary>
    public sealed class IndustrialLoopbackGateway:IDisposable {
        readonly TcpListener listener;readonly IndustrialAuthority authority;readonly Dictionary<string,long> tickets;
        readonly SemaphoreSlim slots=new SemaphoreSlim(16);readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly List<Task> clients=new List<Task>();readonly Task accepting;
        public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
        public IndustrialLoopbackGateway(IndustrialAuthority authority,Dictionary<string,long> tickets){this.authority=authority;this.tickets=new Dictionary<string,long>(tickets);listener=new TcpListener(IPAddress.Loopback,0);listener.Start();accepting=Accept();}
        async Task Accept(){while(!stop.IsCancellationRequested){TcpClient client;try{client=await listener.AcceptTcpClientAsync();}catch(ObjectDisposedException){break;}catch(SocketException)when(stop.IsCancellationRequested){break;}
                if(!slots.Wait(0)){client.Dispose();continue;}lock(clients){clients.RemoveAll(t=>t.IsCompleted);clients.Add(Task.Run(()=>Serve(client)));}}}
        void Serve(TcpClient client){try{using(client){client.ReceiveTimeout=5000;client.SendTimeout=5000;using(var stream=client.GetStream()){
                    var request=Read<IndustrialWireRequest>(stream);IndustrialWireReply reply;
                    if(request.ticket==null||request.ticket.Length>128||!tickets.TryGetValue(request.ticket,out long actor))reply=new IndustrialWireReply{message="Unauthenticated"};
                    else if(request.command?.kind=="haul-start")reply=new IndustrialWireReply{message="Truck jobs require the authoritative world navigation adapter"};
                    else {var result=authority.Execute(actor,request.command);reply=new IndustrialWireReply{accepted=result.accepted,message=result.message,entity=result.entity};}
                    Write(stream,reply);
                }}}catch(Exception e)when(e is IOException||e is InvalidOperationException||e is XmlException||e is SocketException){}finally{slots.Release();}}
        public static IndustrialWireReply Send(int port,string ticket,IndustrialCommand command){using(var client=new TcpClient()){client.Connect(IPAddress.Loopback,port);client.ReceiveTimeout=5000;client.SendTimeout=5000;using(var stream=client.GetStream()){Write(stream,new IndustrialWireRequest{ticket=ticket,command=command});return Read<IndustrialWireReply>(stream);}}}
        static void Write<T>(Stream stream,T data){byte[] bytes;using(var text=new StringWriter()){new XmlSerializer(typeof(T)).Serialize(text,data);bytes=Encoding.UTF8.GetBytes(text.ToString());}if(bytes.Length>65536)throw new InvalidDataException("Command too large");var prefix=BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));stream.Write(prefix,0,4);stream.Write(bytes,0,bytes.Length);stream.Flush();}
        static T Read<T>(Stream stream){var prefix=new byte[4];ReadExact(stream,prefix);int length=IPAddress.NetworkToHostOrder(BitConverter.ToInt32(prefix,0));if(length<1||length>65536)throw new InvalidDataException("Invalid packet size");var bytes=new byte[length];ReadExact(stream,bytes);using(var text=new StringReader(Encoding.UTF8.GetString(bytes)))using(var reader=XmlReader.Create(text,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=65536}))return (T)new XmlSerializer(typeof(T)).Deserialize(reader);}
        static void ReadExact(Stream stream,byte[] bytes){int n=0;while(n<bytes.Length){int count=stream.Read(bytes,n,bytes.Length-n);if(count==0)throw new EndOfStreamException();n+=count;}}
        public void Dispose(){stop.Cancel();listener.Stop();accepting.GetAwaiter().GetResult();Task[] tasks;lock(clients)tasks=clients.ToArray();Task.WaitAll(tasks);slots.Dispose();stop.Dispose();}
    }
}
