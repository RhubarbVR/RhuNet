using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RhuNetShared;

namespace HandShakeServer
{
    class Program
    {
        static readonly int _port = 50;

        static readonly IPEndPoint _tCPEndPoint = new IPEndPoint(IPAddress.Any, _port);
        static readonly TcpListener _tCP = new TcpListener(_tCPEndPoint);

        static IPEndPoint _uDPEndPoint = new(IPAddress.Any, _port);
        static readonly UdpClient _uDP = new UdpClient(_uDPEndPoint);

        static readonly List<ClientInfo> _clients = new List<ClientInfo>();

#pragma warning disable IDE0060 // Remove unused parameter
        static void Main(string[] args)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            var ThreadTCP = new Thread(new ThreadStart(TCPListen));
            var ThreadUDP = new Thread(new ThreadStart(UDPListen));

            ThreadTCP.Start();
            ThreadUDP.Start();

        e: Console.WriteLine("Type 'exit' to shutdown the server");

            if (Console.ReadLine().ToUpper() == "EXIT")
            {
                Console.WriteLine("Shutting down...");
                BroadcastTCP(new Notification(NotificationsTypes.ServerShutdown, null));
                Environment.Exit(0);
            }
            else
            {
                goto e;
            }
        }

        static ClientInfo FindClient(long ID)
        {
            foreach (var item in _clients)
            {
                if(item.ID == ID)
                {
                    return item;
                }
            }
            return null;
        }

        static void TCPListen()
        {
            _tCP.Start();

            Console.WriteLine("TCP Listener Started");

            while (true)
            {
                try
                {
                    var NewClient = _tCP.AcceptTcpClient();

                    var ProcessData = new Action<object>(delegate (object _Client)
                    {
                        var Client = (TcpClient)_Client;
                        Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                        var Data = new byte[4096];
                        var BytesRead = 0;

                        while (Client.Connected)
                        {
                            try
                            {
                                BytesRead = Client.GetStream().Read(Data, 0, Data.Length);
                            }
                            catch
                            {
                                Disconnect(Client);
                            }

                            if (BytesRead == 0)
                            {
                                break;
                            }
                            else if (Client.Connected)
                            {
                                var Item = Data.ToP2PBase();
                                ProcessItem(Item, ProtocolType.Tcp, null, Client);
                            }
                        }

                        Disconnect(Client);
                    });

                    var ThreadProcessData = new Thread(new ParameterizedThreadStart(ProcessData));
                    ThreadProcessData.Start(NewClient);
                }
                catch (Exception ex)
                {
                    Console.Write("TCP Error: {0}", ex.Message);
                }
            }
        }

        static void Disconnect(TcpClient Client)
        {
            var CI = _clients.FirstOrDefault(x => x.Client == Client);

            if (CI != null)
            {
                _clients.Remove(CI);
                Console.WriteLine("Client Disconnected {0}", Client.Client.RemoteEndPoint.ToString());
                Client.Close();

                BroadcastTCP(new Notification(NotificationsTypes.Disconnected, CI.ID));
            }
        }

        static void UDPListen()
        {
            Console.WriteLine("UDP Listener Started");

            while (true)
            {
                byte[] ReceivedBytes = null;

                try
                {
                    ReceivedBytes = _uDP.Receive(ref _uDPEndPoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("UDP Error: {0}", ex.Message);
                }

                if (ReceivedBytes != null)
                {
                    var Item = ReceivedBytes.ToP2PBase();
                    ProcessItem(Item, ProtocolType.Udp, _uDPEndPoint);
                }
            }
        }

        static void ProcessItem(IP2PBase Item, ProtocolType Protocol, IPEndPoint EP = null, TcpClient Client = null)
        {
            if (Item.GetType() == typeof(ClientInfo))
            {
                var CI = _clients.FirstOrDefault(x => x.ID == ((ClientInfo)Item).ID);

                if (CI == null)
                {
                    CI = (ClientInfo)Item;
                    _clients.Add(CI);

                    if (EP != null)
                    {
                        Console.WriteLine("Client Added: UDP EP: {0}:{1}, Name: {2}", EP.Address, EP.Port, CI.Name);
                    }
                    else if (Client != null)
                    {
                        Console.WriteLine("Client Added: TCP EP: {0}:{1}, Name: {2}", ((IPEndPoint)Client.Client.RemoteEndPoint).Address, ((IPEndPoint)Client.Client.RemoteEndPoint).Port, CI.Name);
                    }
                }
                else
                {
                    CI.Update((ClientInfo)Item);

                    if (EP != null)
                    {
                        Console.WriteLine("Client Updated: UDP EP: {0}:{1}, Name: {2}", EP.Address, EP.Port, CI.Name);
                    }
                    else if (Client != null)
                    {
                        Console.WriteLine("Client Updated: TCP EP: {0}:{1}, Name: {2}", ((IPEndPoint)Client.Client.RemoteEndPoint).Address, ((IPEndPoint)Client.Client.RemoteEndPoint).Port, CI.Name);
                    }
                }

                if (EP != null)
                {
                    CI.ExternalEndpoint = EP;
                }

                if (Client != null)
                {
                    CI.Client = Client;
                }

                if (!CI.Initialized)
                {
                    if (CI.ExternalEndpoint != null & Protocol == ProtocolType.Udp)
                        {
                        SendUDP(new Message("Server", CI.Name, "UDP Communication Test"), CI.ExternalEndpoint);
                    }

                    if (CI.Client != null & Protocol == ProtocolType.Tcp)
                        {
                        SendTCP(new Message("Server", CI.Name, "TCP Communication Test"), CI.Client);
                    }

                    if (CI.Client != null & CI.ExternalEndpoint != null)
                    {
                        foreach (var ci in _clients)
                        {
                            SendUDP(ci, CI.ExternalEndpoint);
                        }

                        CI.Initialized = true;
                    }
                }
            }
            else if (Item.GetType() == typeof(Message))
            {
                Console.WriteLine("Message from {0}:{1}: {2}", _uDPEndPoint.Address, _uDPEndPoint.Port, ((Message)Item).Content);
            }
            else if (Item.GetType() == typeof(Req))
            {
                var R = (Req)Item;

                var CI = _clients.FirstOrDefault(x => x.ID == R.RecipientID);

                if (CI != null)
                    {
                    SendTCP(R, CI.Client);
                }
            }
            else if(Item.GetType() == typeof(GetClient))
            {
                Console.WriteLine("get client");
                var e =FindClient(((GetClient)Item).ClientID);
                if(e != null)
                {
                    var CI = _clients.FirstOrDefault(x => x.Client == Client);
                    Console.WriteLine("found client");
                    SendTCP(CI, e.Client);
                    SendTCP(e, Client);
                }
            }
        }

        static void SendTCP(IP2PBase Item, TcpClient Client)
        {
            if (Client != null && Client.Connected)
            {
                var Data = Item.ToByteArray();

                var NetStream = Client.GetStream();
                NetStream.Write(Data, 0, Data.Length);
            }
        }

#pragma warning disable IDE0060 // Remove unused parameter
        static void SendUDP(IP2PBase Item, IPEndPoint EP)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            var Bytes = Item.ToByteArray();
            _uDP.Send(Bytes, Bytes.Length, _uDPEndPoint);
        }

        static void BroadcastTCP(IP2PBase Item)
        {
            foreach (var CI in _clients.Where(x => x.Client != null))
            {
                SendTCP(Item, CI.Client);
            }
        }

        static void BroadcastUDP(IP2PBase Item)
        {
            foreach (var CI in _clients)
            {
                SendUDP(Item, CI.ExternalEndpoint);
            }
        }
    }
}


